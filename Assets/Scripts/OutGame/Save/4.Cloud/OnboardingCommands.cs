using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

/// <summary>온보딩 경제 행동의 재시작 가능한 실행 기록.</summary>
internal static class OnboardingCommands
{
    static string s_savedTxId;
    internal static bool HasPending => Journal != null && !Journal.Completed;
    internal static bool UploadsHeld { get; private set; }
    static OnboardingCommandSaveData Journal => DataSaveManager.Data?.Tutorial?.OnboardingCommand;

    /// <summary>미확정 실행은 서버 처리 기록부터 조회한다. 취소는 UI 기다림만 끝낸다.</summary>
    internal static UniTask RecoverPendingAsync(CancellationToken _ct)
        => ServerSaveCommands.RecoverOnboardingAsync().AttachExternalCancellation(_ct);

    internal static bool Tracks(string _command)
    {
        if (!OnboardingSession.IsActive || !OutgameTutorialGuide.TryGetCurrentStep(out var t_step)) return false;
        switch (_command)
        {
            case "grantTutorialCards": return t_step.Action == EOutgameTutorialAction.DeckGrant ||
                t_step.Action == EOutgameTutorialAction.CardGrant || t_step.Action == EOutgameTutorialAction.CardSetGrant;
            case "openPack": return t_step.Action == EOutgameTutorialAction.AutoPurchase || t_step.Action == EOutgameTutorialAction.WaitPurchase;
            case "enhanceCard":
            case "enhanceSynergyIntroduction":
            case "limitBreakCard": return t_step.Action == EOutgameTutorialAction.WaitEnhance;
            case "enhanceKeyword": return t_step.Action == EOutgameTutorialAction.WaitKeywordEnhance;
            default: return false;
        }
    }

    internal static bool HasReplay(string _command, object _request)
    {
        OnboardingCommandSaveData t_record = Journal;
        return Tracks(_command) && t_record != null && !t_record.Consumed && t_record.Command == _command &&
            t_record.StepId == OnboardingSession.CurrentStepId &&
            JToken.DeepEquals(JToken.Parse(t_record.ArgumentsJson), JToken.FromObject(CallablePayload.ToPrimitiveMap(_request)));
    }

    /// <summary>같은 구매의 개봉·획득 스텝에서만 저장된 결과의 연출을 재개한다.</summary>
    internal static bool TryRestorePackPresentation(out OpenedPack _opened, out string _packId)
    {
        _opened = default;
        _packId = null;
        OnboardingCommandSaveData t_record = Journal;
        if (t_record == null || !t_record.Completed || t_record.Consumed || t_record.Command != "openPack" ||
            string.IsNullOrEmpty(t_record.ResultJson) || !OutgameTutorialGuide.TryGetCurrentStep(out var t_current) ||
            !IsPackPresentation(t_current)) return false;
        if (!HasPackPresentationPath(t_record.StepId, t_current.StepId)) return false;

        JObject t_args = JObject.Parse(t_record.ArgumentsJson);
        if ((string)t_args["env"] != ContentProfileConfig.Active.CloudEnvId) return false;
        _packId = (string)t_args["packId"];
        if (string.IsNullOrEmpty(_packId)) return false;
        return CardPackOpener.TryRestorePresentation(ReadResult<OpenPackResult>(t_record), out _opened);
    }

    internal static async UniTask<OnboardingCommandSaveData> PrepareAsync(string _command, Dictionary<string, object> _args)
    {
        PlayerSaveCloud.CommandSession t_session = PlayerSaveCloud.CaptureCommandSession();
        string t_args = JsonConvert.SerializeObject(_args);
        OnboardingCommandSaveData t_record = Journal;
        if (t_record != null && t_record.StepId == OnboardingSession.CurrentStepId &&
            t_record.Command == _command && JToken.DeepEquals(JToken.Parse(t_record.ArgumentsJson), JToken.Parse(t_args)) &&
            !t_record.Consumed)
        {
            if (!t_record.Completed && s_savedTxId != t_record.TxId && !await PlayerSaveCloud.FlushConfirmedAsync(FirebaseManager.Lifetime))
                throw new InvalidOperationException("The recovered onboarding record could not be saved.");
            PlayerSaveCloud.RequireCommandSession(t_session);
            s_savedTxId = t_record.TxId;
            return t_record;
        }
        if (HasPending) throw new InvalidOperationException("An onboarding command still needs recovery.");

        t_record = new OnboardingCommandSaveData
        {
            StepId = OnboardingSession.CurrentStepId, Command = _command,
            TxId = $"{_command}:{Guid.NewGuid():N}", ArgumentsJson = t_args
        };
        DataSaveManager.Data.Tutorial.OnboardingCommand = t_record;
        DataSaveManager.Save();
        if (!await PlayerSaveCloud.FlushConfirmedAsync(FirebaseManager.Lifetime))
            throw new InvalidOperationException("The onboarding command record could not be saved.");
        PlayerSaveCloud.RequireCommandSession(t_session);
        s_savedTxId = t_record.TxId;
        return t_record;
    }

    internal static async UniTask RecoverUnderGateAsync(ICallableService _service)
    {
        PlayerSaveCloud.CommandSession t_session = PlayerSaveCloud.CaptureCommandSession();
        OnboardingCommandSaveData t_record = Journal;
        if (t_record == null || t_record.Completed) return;
        var t_args = CallablePayload.ToPrimitiveMap(JObject.Parse(t_record.ArgumentsJson));
        if (!t_args.TryGetValue("env", out object t_env) || !string.Equals(t_env as string, t_session.EnvId, StringComparison.Ordinal))
            throw new ServerAdoptionException("The onboarding command belongs to another environment.");
        await PlayerSaveCloud.SuspendUploadsAsync();
        try
        {
            PlayerSaveCloud.RequireCommandSession(t_session);
            var t_response = await _service.InvokeAsync<OnboardingOperationResponse>("getOnboardingOperation",
                new { env = t_session.EnvId, txId = t_record.TxId, command = t_record.Command, args = t_args });
            PlayerSaveCloud.RequireCommandSession(t_session);
            if (t_response == null || t_response.Current?.Save == null)
                throw new InvalidOperationException("Onboarding recovery returned no current snapshot.");
            PlayerSaveCloud.ValidateOnboardingSnapshot(t_response.Current, t_session);
            s_savedTxId = (string)t_response.Current.Save["tutorial"]?["onboardingCommand"]?["txId"];
            if (!t_response.Found && s_savedTxId == t_record.TxId)
            {
                var t_retry = new Dictionary<string, object>(t_args)
                {
                    ["txId"] = t_record.TxId,
                    ["onboarding"] = true
                };
                try
                {
                    PlayerSaveCloud.RequireCommandSession(t_session);
                    await _service.InvokeAsync<JObject>(t_record.Command, t_retry);
                    PlayerSaveCloud.RequireCommandSession(t_session);
                }
                catch (Exception t_error) when (t_error.GetBaseException().Message.Contains("OnboardingRecoveryRequired")) { }
                catch (Exception t_error) when (CloudFailureClassifier.Classify(t_error) == ECloudFailureKind.Rejected)
                {
                    PlayerSaveCloud.AdoptOnboardingSnapshot(t_response.Current, t_session);
                    Reject(t_record);
                    return;
                }
                PlayerSaveCloud.RequireCommandSession(t_session);
                t_response = await _service.InvokeAsync<OnboardingOperationResponse>("getOnboardingOperation",
                    new { env = t_session.EnvId, txId = t_record.TxId, command = t_record.Command, args = t_args });
                PlayerSaveCloud.RequireCommandSession(t_session);
                if (t_response == null || !t_response.Found || t_response.Current?.Save == null)
                    throw new InvalidOperationException("The onboarding command remains unconfirmed.");
            }
            PlayerSaveCloud.AdoptOnboardingSnapshot(t_response.Current, t_session);
            UploadsHeld = false;
            if (!t_response.Found)
            {
                // 실행 기록 저장 전에 실패했다. 아직 전송하지 않은 의도만 새로 확정한다.
                return;
            }
            if (t_response.Operation?.Result == null || t_response.Operation.Command != t_record.Command ||
                t_response.Operation.TxId != t_record.TxId)
                throw new InvalidOperationException("Onboarding recovery returned a mismatched operation.");
            t_record.ResultJson = t_response.Operation.Result.ToString(Formatting.None);
            t_record.Completed = true;
            t_record.Consumed = false;
            DataSaveManager.Data.Tutorial.OnboardingCommand = t_record;
            DataSaveManager.Save();
        }
        catch
        {
            if (PlayerSaveCloud.IsCommandSessionCurrent(t_session)) UploadsHeld = true;
            throw;
        }
        finally
        {
            if (PlayerSaveCloud.IsCommandSessionCurrent(t_session)) PlayerSaveCloud.ResumeUploads();
        }
    }

    internal static TResponse ReadResult<TResponse>(OnboardingCommandSaveData _record) where TResponse : class
        => CallablePayload.ToResponse<TResponse>(JObject.Parse(_record.ResultJson));

    internal static void Complete(OnboardingCommandSaveData _record, ServerCommandResult _result)
    {
        if (_record == null) return;
        UploadsHeld = false;
        JObject t_result = JObject.FromObject(_result, JsonSerializer.Create(DataSaveManager.SaveSerializerSettings));
        t_result.Remove("updatedSlots");
        t_result.Remove("wallet");
        t_result.Remove("missions");
        _record.ResultJson = t_result.ToString(Formatting.None);
        _record.Completed = true;
        DataSaveManager.Data.Tutorial.OnboardingCommand = _record;
        DataSaveManager.Save();
    }

    internal static void Consume(OnboardingCommandSaveData _record)
    {
        // 샤드 투입은 목표까지 한 스텝에서 여러 번 실행된다. 지급·구매는 스텝 전환까지 재생한다.
        if (_record == null || !(_record.Command.StartsWith("enhance", StringComparison.Ordinal) || _record.Command == "limitBreakCard")) return;
        _record.Consumed = true;
        DataSaveManager.Save();
    }

    internal static void Reject(OnboardingCommandSaveData _record)
    {
        if (_record == null) return;
        UploadsHeld = false;
        _record.Completed = true;
        _record.Consumed = true;
        DataSaveManager.Save();
    }

    internal static void HoldUploads() { UploadsHeld = true; }
    internal static void ResetSession() { UploadsHeld = false; s_savedTxId = null; }

    static bool IsPackPresentation(TutorialStepDef _step)
        => _step.Action == EOutgameTutorialAction.WaitPackOpen ||
            (_step.Action == EOutgameTutorialAction.WaitClick && _step.Anchor == EOutgameTutorialAnchor.PackAcquireButton);

    static bool HasPackPresentationPath(int _purchaseStepId, int _currentStepId)
    {
        for (int t_chapter = 0; t_chapter < OutgameTutorialRunner.ChapterCount; t_chapter++)
        {
            bool t_purchased = false;
            for (int t_index = 0; OutgameTutorialRunner.TryGetStepForDebug(t_chapter, t_index, out var t_step); t_index++)
            {
                if (t_step.StepId == _purchaseStepId)
                {
                    t_purchased = t_step.Action == EOutgameTutorialAction.AutoPurchase ||
                        t_step.Action == EOutgameTutorialAction.WaitPurchase;
                    continue;
                }
                if (!t_purchased) continue;
                if (!IsPackPresentation(t_step)) break;
                if (t_step.StepId == _currentStepId) return true;
            }
        }
        return false;
    }
}

internal sealed class OnboardingOperationResponse
{
    [JsonProperty("found")] public bool Found { get; set; }
    [JsonProperty("operation")] public OnboardingOperation Operation { get; set; }
    [JsonProperty("current")] public OnboardingCurrentSnapshot Current { get; set; }
}

internal sealed class OnboardingOperation
{
    [JsonProperty("command")] public string Command { get; set; }
    [JsonProperty("txId")] public string TxId { get; set; }
    [JsonProperty("result")] public JObject Result { get; set; }
}

internal sealed class OnboardingCurrentSnapshot
{
    [JsonProperty("save")] public JObject Save { get; set; }
    [JsonProperty("wallet")] public WalletPatch Wallet { get; set; }
    [JsonProperty("missions")] public MissionSnapshot Missions { get; set; }
}
