using System;
using System.Collections.Generic;
using System.Linq;
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
    public readonly List<string> PayloadIds = new List<string>();
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
        var candidates = FindMatchingWorldTokens(slot).ToList();
        ResetCycleOrder(candidates);
        return candidates.FirstOrDefault();
    }

    private Token FindNextMatchingToken(ThresholdSphere slot, Token currentToken)
    {
        var currentPayloadId = GetPayloadId(currentToken);
        if (string.IsNullOrEmpty(currentPayloadId))
            return null;

        var cycleOrder = cycleState.PayloadIds;
        var worldCandidates = FindMatchingWorldTokens(slot).ToList();
        var candidatesByPayloadId = new Dictionary<string, Token>(StringComparer.Ordinal)
        {
            [currentPayloadId] = currentToken
        };

        foreach (var candidate in worldCandidates)
        {
            var payloadId = GetPayloadId(candidate);
            if (!string.IsNullOrEmpty(payloadId) && !candidatesByPayloadId.ContainsKey(payloadId))
                candidatesByPayloadId.Add(payloadId, candidate);
        }

        if (!cycleOrder.Contains(currentPayloadId))
        {
            cycleOrder.Clear();
            cycleOrder.Add(currentPayloadId);
        }

        AddCycleTokens(worldCandidates);
        PruneCycleOrder(candidatesByPayloadId);

        if (cycleOrder.Count < 2)
            return null;

        var currentIndex = cycleOrder.IndexOf(currentPayloadId);
        if (currentIndex < 0)
            return null;

        for (var offset = 1; offset <= cycleOrder.Count; offset++)
        {
            var payloadId = cycleOrder[(currentIndex + offset) % cycleOrder.Count];
            if (payloadId == currentPayloadId)
                continue;

            if (candidatesByPayloadId.TryGetValue(payloadId, out var candidate))
                return candidate;
        }

        return null;
    }

    private static bool TryPlaceToken(ThresholdSphere slot, Token token, Token currentToken, bool slotIsEmpty)
    {
        if (slotIsEmpty)
        {
            return slot.HasEnoughSpaceForToken(token) &&
                   slot.TryAcceptToken(token, new Context(Context.ActionSource.PlayerDrag));
        }

        return slot.TryAcceptToken(token, new Context(Context.ActionSource.PlayerDrag)) ||
               slot.TryMoveAsideAndAcceptToken(token, currentToken);
    }

    private static Token GetCurrentToken(ThresholdSphere slot)
    {
        return slot.GetElementTokens()
            .FirstOrDefault(token => token != null && token.IsValidElementStack());
    }

    private static IEnumerable<Token> FindMatchingWorldTokens(ThresholdSphere slot)
    {
        var slotPosition = slot.GetRectTransform().anchoredPosition;

        return GetWorldElementTokens()
            .Where(token => token != null && token.IsValidElementStack())
            .Where(token => token.TokenRectTransform != null)
            .Where(token => slot.GetMatchForTokenPayload(token.Payload).MatchType == SlotMatchForAspectsType.Okay)
            .OrderBy(token => DistanceSquared(token.TokenRectTransform.anchoredPosition, slotPosition))
            .ToList();
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
        cycleState.PayloadIds.Clear();
        AddCycleTokens(tokens);
    }

    private void AddCycleTokens(IEnumerable<Token> tokens)
    {
        foreach (var token in tokens)
            AddCycleToken(token);
    }

    private void AddCycleToken(Token token)
    {
        var payloadId = GetPayloadId(token);
        if (!string.IsNullOrEmpty(payloadId) && !cycleState.PayloadIds.Contains(payloadId))
            cycleState.PayloadIds.Add(payloadId);
    }

    private void PruneCycleOrder(Dictionary<string, Token> candidatesByPayloadId)
    {
        var cycleOrder = cycleState.PayloadIds;
        for (var i = cycleOrder.Count - 1; i >= 0; i--)
        {
            if (!candidatesByPayloadId.ContainsKey(cycleOrder[i]))
                cycleOrder.RemoveAt(i);
        }
    }

    private static string GetPayloadId(Token token)
    {
        return token == null ? null : token.PayloadId;
    }

    private static float DistanceSquared(Vector2 a, Vector2 b)
    {
        var dx = a.x - b.x;
        var dy = a.y - b.y;
        return dx * dx + dy * dy;
    }
}
