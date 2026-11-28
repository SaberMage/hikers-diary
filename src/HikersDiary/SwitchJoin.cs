using HarmonyLib;
using HikersDiary.Core;
using Mirror;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HikersDiary;

/// <summary>
/// The join of a switch (#14, T3d). The first screen in the switch mode calls <see cref="Start"/> in its host step.
/// The fade-out cover stays until the local player exists. If the join fails, the fallback hosts a local save.
/// </summary>
// [impl->REQ-FRIEND-JOIN]
internal static class SwitchJoin
{
    private const float TimeoutSeconds = 30f;
    private static SwitchTarget _target;
    private static string _password;
    private static System.Action _fallback;
    private static float _at, _failAt;
    private const float GuardSeconds = 15f;
    private static string _guardCode;
    private static float _guardUntil = -1f;
    private static bool _passwordSent, _failed;

    internal static bool Joining => _target != null;

    /// <summary>True when the session of the join started: the local player exists.</summary>
    internal static bool Started => _target != null && !_failed && NetworkClient.isConnected && DiaryHost.LocalPlayer() != null;

    internal static void Start(SwitchTarget target, string password, System.Action fallback)
    {
        var menus = Resources.FindObjectsOfTypeAll<JoinMenu>();
        if (menus == null || menus.Length == 0)
        {
            Plugin.Logger.LogWarning("Diary: switch found no JoinMenu.");
            fallback();
            return;
        }
        _target = target;
        _password = password;
        _fallback = fallback;
        _at = Time.time;
        _passwordSent = _failed = false;
        GameStart.KeepCanvas(); // the game unloads BaseScene when the world of the host loads
        NetworkMinder.SetServerPassword(password);
        Plugin.Logger.LogInfo($"Diary: switch joins {target.Code} ('{target.Title}'), {(password.Length > 0 ? "with a" : "no")} password.");
        menus[0].ConnectTo(target.Code);
    }

    internal static void Step()
    {
        GuardCode();
        if (_target == null) return;
        if (Started)
        {
            string title = EOSPrivateLobbyManager.Instance?.GetJoinedLobbyInfo()?.worldName;
            Plugin.Logger.LogInfo($"Diary: switch joined {_target.Code} ('{title}') {Time.time - _at:0.0} s after the join call.");
            SaveSwitch.KeepPassword(_target.Code, string.IsNullOrEmpty(title) ? _target.Title : title, _password);
            _target = null;
            return;
        }
        if (!_failed && !_passwordSent)
        {
            PasswordMenu pm = Object.FindObjectOfType<PasswordMenu>();
            if (pm != null)
            {
                _passwordSent = true;
                pm.passwordField.text = _password;
                pm.SubmitPassword();
                Plugin.Logger.LogInfo($"Diary: switch sent the password {Time.time - _at:0.0} s after the join call.");
            }
        }
        if (!_failed && Time.time - _at >= TimeoutSeconds) Fail("no session after 30 s");
        if (!_failed) return;
        // The game stops the client after its error menu. Stop it here only if the game did not.
        float since = Time.time - _failAt;
        if (NetworkClient.active)
        {
            if (since >= 1f) NetworkManager.singleton.StopClient();
            return;
        }
        System.Action f = _fallback;
        _target = null;
        _fallback = null;
        f?.Invoke();
        _guardCode = EOSPrivateLobbyManager.Instance?.CurrentLobbyCode;
        _guardUntil = Time.time + GuardSeconds;
    }

    // The game leaves the joined lobby some seconds after the failure. The end of the leave (inline in the leave
    // callback, no method to patch) sets currentLobbyCode to null, also after the fallback host made its code. Then
    // the host lobby has no code (test A, 2026-11-28). Thus put the code of the fallback host back.
    private static void GuardCode()
    {
        var mgr = EOSPrivateLobbyManager.Instance;
        if (Time.time > _guardUntil || string.IsNullOrEmpty(_guardCode) || mgr == null || !NetworkServer.active) return;
        if (!string.IsNullOrEmpty(mgr.CurrentLobbyCode)) return;
        mgr.currentLobbyCode = _guardCode;
        Plugin.Logger.LogInfo($"Diary: switch put the join code {_guardCode} back after the leave of the failed join.");
    }

    internal static void Fail(string why)
    {
        if (_target == null || _failed) return;
        _failed = true;
        _failAt = Time.time;
        // Do not stop the client here: the game is possibly in its auth handler, and it uses the client after it.
        Plugin.Logger.LogWarning($"Diary: switch join of {_target.Code} failed ({why}) {Time.time - _at:0.0} s after the join call.");
    }
}

/// <summary>During the join of a switch, the error menu of the game does not show. The join fails.</summary>
// [impl->REQ-FRIEND-JOIN]
[HarmonyPatch(typeof(MainMenuManager), nameof(MainMenuManager.ShowErrorMenu))]
internal static class SwitchJoinErrorPatch
{
    private static bool Prefix(ErrorUtils.ErrorType errorType)
    {
        if (!SwitchJoin.Joining) return true;
        SwitchJoin.Fail($"error menu {errorType}");
        return false;
    }
}
