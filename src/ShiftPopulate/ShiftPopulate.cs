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
        }
    }
}

public sealed class ShiftPopulateSlotClickHandler : MonoBehaviour, IPointerClickHandler
{
    private ThresholdSphere threshold;

    public void Bind(ThresholdSphere thresholdSphere)
    {
        threshold = thresholdSphere;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        try
        {
            if (!ShiftHeld())
                return;

            if (threshold == null || !threshold.IsEmpty())
                return;

            var numa = Watchman.Get<Numa>();
            if (numa != null && numa.IsOtherworldActive())
                return;

            var token = FindNearestMatchingToken(threshold);
            if (token == null)
                return;

            if (threshold.HasEnoughSpaceForToken(token) &&
                threshold.TryAcceptToken(token, new Context(Context.ActionSource.PlayerDrag)))
            {
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

    private static Token FindNearestMatchingToken(ThresholdSphere slot)
    {
        var slotPosition = slot.GetRectTransform().anchoredPosition;

        return GetWorldElementTokens()
            .Where(token => token != null && token.IsValidElementStack())
            .Where(token => token.TokenRectTransform != null)
            .Where(token => slot.GetMatchForTokenPayload(token.Payload).MatchType == SlotMatchForAspectsType.Okay)
            .OrderBy(token => DistanceSquared(token.TokenRectTransform.anchoredPosition, slotPosition))
            .FirstOrDefault();
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

    private static float DistanceSquared(Vector2 a, Vector2 b)
    {
        var dx = a.x - b.x;
        var dy = a.y - b.y;
        return dx * dx + dy * dy;
    }
}
