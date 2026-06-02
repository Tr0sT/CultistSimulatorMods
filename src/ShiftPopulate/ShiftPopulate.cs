using System;
using System.Collections.Generic;
using System.Linq;
using SecretHistories.Core;
using SecretHistories.Entities;
using SecretHistories.Enums;
using SecretHistories.Spheres;
using SecretHistories.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

public sealed class ShiftPopulate : MonoBehaviour
{
    private const float ScanInterval = 0.25f;
    private float nextScan;

    public static void Initialise()
    {
        try
        {
            var existing = FindObjectOfType<ShiftPopulate>();
            if (existing != null)
                return;

            var manager = new GameObject("ShiftPopulate");
            DontDestroyOnLoad(manager);
            manager.AddComponent<ShiftPopulate>();
            Debug.Log("[ShiftPopulate] Initialised.");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    private void Update()
    {
        if (Time.realtimeSinceStartup < nextScan)
            return;

        nextScan = Time.realtimeSinceStartup + ScanInterval;
        AttachHandlers();
    }

    private static void AttachHandlers()
    {
        foreach (var threshold in FindObjectsOfType<ThresholdSphere>())
        {
            if (threshold == null || threshold.gameObject == null)
                continue;

            var handler = threshold.GetComponent<ShiftPopulateSlotClickHandler>();
            if (handler == null)
                handler = threshold.gameObject.AddComponent<ShiftPopulateSlotClickHandler>();

            handler.Bind(threshold);

            foreach (var token in threshold.GetElementTokens())
            {
                if (token == null || token.gameObject == null)
                    continue;

                var tokenHandler = token.GetComponent<ShiftPopulateSlotClickHandler>();
                if (tokenHandler == null)
                    tokenHandler = token.gameObject.AddComponent<ShiftPopulateSlotClickHandler>();

                tokenHandler.Bind(threshold, token);
            }
        }
    }
}

public sealed class ShiftPopulateSlotCycleState : MonoBehaviour
{
    public readonly List<string> CardIds = new List<string>();
}

public sealed class ShiftPopulateSlotClickHandler : MonoBehaviour, IPointerClickHandler
{
    private ThresholdSphere threshold;
    private Token sourceToken;
    private ShiftPopulateSlotCycleState cycleState;

    public void Bind(ThresholdSphere thresholdSphere, Token token = null)
    {
        threshold = thresholdSphere;
        sourceToken = token;
        cycleState = thresholdSphere.GetComponent<ShiftPopulateSlotCycleState>();
        if (cycleState == null)
            cycleState = thresholdSphere.gameObject.AddComponent<ShiftPopulateSlotCycleState>();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        try
        {
            if (!ShiftHeld())
                return;

            if (threshold == null)
                return;

            if (sourceToken != null && sourceToken.Sphere != threshold)
                return;

            var numa = Watchman.Get<Numa>();
            if (numa != null && numa.IsOtherworldActive())
                return;

            var currentToken = GetCurrentToken(threshold);
            var slotIsEmpty = currentToken == null || threshold.IsEmpty();
            var token = slotIsEmpty
                ? FindFirstMatchingToken(threshold)
                : FindNextMatchingToken(threshold, currentToken);

            if (token == null)
                return;

            if (TryPlaceToken(threshold, token, currentToken, slotIsEmpty))
            {
                AddCycleToken(token);
                eventData.Use();
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    private static bool ShiftHeld()
    {
        var keyboard = Keyboard.current;
        if (keyboard != null &&
            (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed))
        {
            return true;
        }

        try
        {
            return UnityEngine.Input.GetKey(KeyCode.LeftShift) ||
                   UnityEngine.Input.GetKey(KeyCode.RightShift);
        }
        catch
        {
            return false;
        }
    }

    private Token FindFirstMatchingToken(ThresholdSphere slot)
    {
        var candidates = FindMatchingWorldTokens(slot, null).ToList();
        ResetCycleOrder(candidates);
        return candidates.FirstOrDefault();
    }

    private Token FindNextMatchingToken(ThresholdSphere slot, Token currentToken)
    {
        var currentCardId = GetCardId(currentToken);
        if (string.IsNullOrEmpty(currentCardId))
            return null;

        var cycleOrder = cycleState.CardIds;
        var worldCandidates = FindMatchingWorldTokens(slot, currentToken).ToList();
        var candidatesByCardId = new Dictionary<string, Token>(StringComparer.Ordinal)
        {
            [currentCardId] = currentToken
        };

        foreach (var candidate in worldCandidates)
        {
            var cardId = GetCardId(candidate);
            if (!string.IsNullOrEmpty(cardId) && !candidatesByCardId.ContainsKey(cardId))
                candidatesByCardId.Add(cardId, candidate);
        }

        if (!cycleOrder.Contains(currentCardId))
        {
            cycleOrder.Clear();
            cycleOrder.Add(currentCardId);
        }

        AddCycleTokens(worldCandidates);
        PruneCycleOrder(candidatesByCardId);

        if (cycleOrder.Count < 2)
            return null;

        var currentIndex = cycleOrder.IndexOf(currentCardId);
        if (currentIndex < 0)
            return null;

        for (var offset = 1; offset <= cycleOrder.Count; offset++)
        {
            var cardId = cycleOrder[(currentIndex + offset) % cycleOrder.Count];
            if (cardId == currentCardId)
                continue;

            if (candidatesByCardId.TryGetValue(cardId, out var candidate))
                return candidate;
        }

        return null;
    }

    private static bool TryPlaceToken(ThresholdSphere slot, Token token, Token currentToken, bool slotIsEmpty)
    {
        var cardId = GetCardId(token);
        if (string.IsNullOrEmpty(cardId))
            return false;

        if (slotIsEmpty)
        {
            if (!slot.HasEnoughSpaceForToken(token))
                return false;

            slot.TryAcceptToken(token, new Context(Context.ActionSource.PlayerDrag));
        }
        else
        {
            slot.TryMoveAsideAndAcceptToken(token, currentToken);
        }

        return SlotContainsCard(slot, cardId);
    }

    private static Token GetCurrentToken(ThresholdSphere slot)
    {
        return slot.GetElementTokens()
            .FirstOrDefault(token => token != null && token.IsValidElementStack());
    }

    private static IEnumerable<Token> FindMatchingWorldTokens(ThresholdSphere slot, Token currentToken)
    {
        var slotPosition = slot.GetRectTransform().anchoredPosition;

        var candidates = GetWorldElementTokens()
            .Where(token => token != null && token.IsValidElementStack())
            .Where(token => token.TokenRectTransform != null)
            .Where(token => slot.GetMatchForTokenPayload(token.Payload).MatchType == SlotMatchForAspectsType.Okay)
            .OrderBy(token => DistanceSquared(token.TokenRectTransform.anchoredPosition, slotPosition))
            .ToList();

        var uniqueCandidates = CollapseDuplicateCards(candidates).ToList();
        return ApplyRecipeStartFilter(slot, currentToken, uniqueCandidates);
    }

    private static IEnumerable<Token> GetWorldElementTokens()
    {
        var hornedAxe = Watchman.Get<HornedAxe>();
        if (hornedAxe == null)
            yield break;

        foreach (var sphere in hornedAxe.GetSpheres())
        {
            if (sphere == null || sphere.SphereCategory != SphereCategory.World || sphere.Shrouded)
                continue;

            foreach (var stack in sphere.GetElementStacks())
            {
                if (stack != null)
                    yield return stack.Token;
            }
        }
    }

    private void ResetCycleOrder(IEnumerable<Token> tokens)
    {
        cycleState.CardIds.Clear();
        AddCycleTokens(tokens);
    }

    private void AddCycleTokens(IEnumerable<Token> tokens)
    {
        foreach (var token in tokens)
            AddCycleToken(token);
    }

    private void AddCycleToken(Token token)
    {
        var cardId = GetCardId(token);
        if (!string.IsNullOrEmpty(cardId) && !cycleState.CardIds.Contains(cardId))
            cycleState.CardIds.Add(cardId);
    }

    private void PruneCycleOrder(Dictionary<string, Token> candidatesByCardId)
    {
        var cycleOrder = cycleState.CardIds;
        for (var i = cycleOrder.Count - 1; i >= 0; i--)
        {
            if (!candidatesByCardId.ContainsKey(cycleOrder[i]))
                cycleOrder.RemoveAt(i);
        }
    }

    private static IEnumerable<Token> CollapseDuplicateCards(IEnumerable<Token> tokens)
    {
        var seenCardIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var token in tokens)
        {
            var cardId = GetCardId(token);
            if (!string.IsNullOrEmpty(cardId) && seenCardIds.Add(cardId))
                yield return token;
        }
    }

    private static IEnumerable<Token> ApplyRecipeStartFilter(ThresholdSphere slot, Token currentToken, List<Token> candidates)
    {
        var situation = FindOwningUnstartedSituation(slot);
        if (situation == null)
            return candidates;

        if (!IsLastOpenThresholdSlot(situation, slot))
            return candidates;

        var startableOrExpandingCandidates = candidates
            .Where(candidate => CandidateWouldOpenMoreSlots(situation, slot, candidate) ||
                                CanStartWithTokenInSlot(situation, slot, candidate))
            .ToList();

        if (startableOrExpandingCandidates.Count > 0)
            return startableOrExpandingCandidates;

        return candidates;
    }

    private static bool IsLastOpenThresholdSlot(Situation situation, ThresholdSphere slot)
    {
        foreach (var sphere in situation.GetSpheresActiveForCurrentState())
        {
            var threshold = sphere as ThresholdSphere;
            if (threshold == null || threshold == slot)
                continue;

            if (threshold.IsEmpty())
                return false;
        }

        return true;
    }

    private static bool CandidateWouldOpenMoreSlots(Situation situation, ThresholdSphere slot, Token candidate)
    {
        var aspects = GetSituationAspectsWithTokenInSlot(situation, slot, candidate);
        var childSpecs = GetApplicableChildSlotSpecs(situation, slot, candidate, aspects).ToList();

        foreach (var childSpec in childSpecs)
        {
            if (ChildSlotWouldNeedInput(situation, childSpec))
                return true;
        }

        return CanReachRecipeAfterChildSlots(situation, aspects, childSpecs);
    }

    private static IEnumerable<SphereSpec> GetApplicableChildSlotSpecs(
        Situation situation,
        ThresholdSphere slot,
        Token candidate,
        AspectsDictionary aspects)
    {
        var childSpecs = slot.GetChildSpheresSpecsToAddIfThisTokenAdded(candidate, situation.VerbId);
        if (childSpecs == null)
            yield break;

        foreach (var childSpec in childSpecs)
        {
            if (childSpec == null)
                continue;

            if (childSpec.IfAspectsPresent != null &&
                !aspects.SatisfiesRequirements(childSpec.IfAspectsPresent))
            {
                continue;
            }

            yield return childSpec;
        }
    }

    private static bool ChildSlotWouldNeedInput(Situation situation, SphereSpec childSpec)
    {
        if (string.IsNullOrEmpty(childSpec.Id))
            return true;

        var existingSphere = situation.GetSphereById(childSpec.Id);
        var existingThreshold = existingSphere as ThresholdSphere;
        if (existingThreshold == null)
            return true;

        if (!situation.GetSpheresActiveForCurrentState().Contains(existingThreshold))
            return true;

        return existingThreshold.IsEmpty();
    }

    private static bool CanReachRecipeAfterChildSlots(
        Situation situation,
        AspectsDictionary aspects,
        List<SphereSpec> childSpecs)
    {
        if (childSpecs.Count == 0)
            return false;

        var hornedAxe = Watchman.Get<HornedAxe>();
        var compendium = Watchman.Get<Compendium>();
        if (hornedAxe == null || compendium == null || situation.Verb == null)
            return false;

        var futureAspectIds = GetFutureSlotAspectIds(childSpecs);
        if (futureAspectIds.Count == 0)
            return false;

        var context = hornedAxe.GetAspectsInContext(aspects);
        foreach (var recipe in compendium.GetValidRecipesForUnstartedVerb(situation.VerbId))
        {
            if (recipe == null || !recipe.IsValid() || !recipe.Craftable)
                continue;

            if (!RequirementsSatisfiedAllowingFutureSlots(
                    context.AspectsInSituation,
                    recipe.Requirements,
                    futureAspectIds))
            {
                continue;
            }

            if (!RequirementsSatisfied(context.AspectsOnTable, recipe.TableReqs) ||
                !RequirementsSatisfied(context.AspectsExtant, recipe.ExtantReqs))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static HashSet<string> GetFutureSlotAspectIds(IEnumerable<SphereSpec> childSpecs)
    {
        var aspectIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var childSpec in childSpecs)
        {
            AddPositiveAspectIds(aspectIds, childSpec.Required);
            AddPositiveAspectIds(aspectIds, childSpec.Essential);
        }

        return aspectIds;
    }

    private static void AddPositiveAspectIds(HashSet<string> aspectIds, AspectsDictionary aspects)
    {
        if (aspects == null)
            return;

        foreach (var aspect in aspects)
        {
            if (aspect.Value > 0)
                aspectIds.Add(aspect.Key);
        }
    }

    private static bool RequirementsSatisfiedAllowingFutureSlots(
        AspectsDictionary aspects,
        Dictionary<string, string> requirements,
        HashSet<string> futureAspectIds)
    {
        if (requirements == null || requirements.Count == 0)
            return false;

        var reducedRequirements = new Dictionary<string, string>(requirements, StringComparer.Ordinal);
        var removedFutureRequirement = false;
        foreach (var requirement in requirements)
        {
            if (!futureAspectIds.Contains(requirement.Key) ||
                aspects.SatisfiesRequirement(requirement.Key, requirement.Value))
            {
                continue;
            }

            reducedRequirements.Remove(requirement.Key);
            removedFutureRequirement = true;
        }

        return removedFutureRequirement && aspects.SatisfiesRequirements(reducedRequirements);
    }

    private static bool RequirementsSatisfied(
        AspectsDictionary aspects,
        Dictionary<string, string> requirements)
    {
        return requirements == null || aspects.SatisfiesRequirements(requirements);
    }

    private static Situation FindOwningUnstartedSituation(ThresholdSphere slot)
    {
        var hornedAxe = Watchman.Get<HornedAxe>();
        if (hornedAxe == null)
            return null;

        foreach (var situation in hornedAxe.GetRegisteredSituations())
        {
            if (situation == null || situation.StateIdentifier != StateEnum.Unstarted)
                continue;

            if (situation.GetSpheres().Contains(slot))
                return situation;
        }

        return null;
    }

    private static bool CanStartWithTokenInSlot(Situation situation, ThresholdSphere slot, Token tokenInSlot)
    {
        var hornedAxe = Watchman.Get<HornedAxe>();
        var compendium = Watchman.Get<Compendium>();
        var stable = Watchman.Get<Stable>();
        if (hornedAxe == null || compendium == null || stable == null || situation.Verb == null)
            return false;

        var aspects = GetSituationAspectsWithTokenInSlot(situation, slot, tokenInSlot);
        var context = hornedAxe.GetAspectsInContext(aspects);
        var recipe = compendium.GetFirstMatchingRecipe(context, situation.VerbId);
        if (recipe == null || !recipe.IsValid() || !recipe.Craftable)
            return false;

        return recipe.CanExecuteInContext(context, stable.Protag());
    }

    private static AspectsDictionary GetSituationAspectsWithTokenInSlot(Situation situation, ThresholdSphere slot, Token tokenInSlot)
    {
        var aspects = new AspectsDictionary();
        foreach (var sphere in situation.GetInteriorSpheres())
        {
            if (sphere == null)
                continue;

            if (sphere == slot)
            {
                CombineTokenAspects(aspects, tokenInSlot);
                continue;
            }

            foreach (var token in sphere.GetElementTokens())
                CombineTokenAspects(aspects, token);
        }

        if (situation.Verb != null && situation.Verb.Aspects != null)
            aspects.CombineAspects(situation.Verb.Aspects);

        return aspects;
    }

    private static void CombineTokenAspects(AspectsDictionary aspects, Token token)
    {
        if (token != null && token.IsValidElementStack())
            aspects.CombineAspects(token.GetAspects(true));
    }

    private static bool SlotContainsCard(ThresholdSphere slot, string cardId)
    {
        return slot.GetElementTokens()
            .Any(token => GetCardId(token) == cardId);
    }

    private static string GetCardId(Token token)
    {
        if (token == null)
            return null;

        return string.IsNullOrEmpty(token.PayloadEntityId) ? token.PayloadId : token.PayloadEntityId;
    }

    private static float DistanceSquared(Vector2 a, Vector2 b)
    {
        var dx = a.x - b.x;
        var dy = a.y - b.y;
        return dx * dx + dy * dy;
    }
}
