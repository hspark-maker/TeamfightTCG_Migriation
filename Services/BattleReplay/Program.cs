// EmulatorDetection 은 Google.Api.Gax 소속이다 — Firestore using 만으로는 안 잡힌다.
using Google.Api.Gax;
using Google.Cloud.Firestore;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(_options =>
    _options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase);

string projectId = Environment.GetEnvironmentVariable("GOOGLE_CLOUD_PROJECT")
    ?? Environment.GetEnvironmentVariable("GCLOUD_PROJECT")
    ?? "bm-cardbattle";
string databaseId = Environment.GetEnvironmentVariable("FIRESTORE_DATABASE_ID") ?? "cardbattle";

builder.Services.AddSingleton(_ => new FirestoreDbBuilder
{
    ProjectId = projectId,
    DatabaseId = databaseId,
    EmulatorDetection = EmulatorDetection.EmulatorOrProduction,
}.Build());
builder.Services.AddSingleton<BattleSpecRepository>();

WebApplication app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new { ok = true }));

app.MapPost("/v1/battle/replay", async (
    ReplayRequest _request,
    BattleSpecRepository _specs,
    CancellationToken _cancellationToken) =>
{
    if (!_request.TryBuildInput(out BattleReplayInput? t_input, out string t_error))
        return Results.BadRequest(new { ok = false, reason = t_error });

    BattleRuleSet t_rules;
    try
    {
        t_rules = await _specs.GetAsync(
            _request.Env, _request.ContentFingerprint, _request.SpecPins, _cancellationToken);
    }
    catch (ContentFingerprintException t_exception)
    {
        return Results.Conflict(new { ok = false, reason = t_exception.Message });
    }
    catch (SpecLoadException t_exception)
    {
        return Results.Json(new { ok = false, reason = t_exception.Message }, statusCode: 503);
    }

    try
    {
        // 요청마다 specPins 가 달라 규칙 객체가 다르다. 전역 슬롯(Install)에 심으면 동시 요청이
        // 서로 덮어써서 다른 판의 규칙으로 재생한다 — 여기서는 흐름 한정만 쓴다.
        SynergyRuleProvider.InstallScoped(t_rules);
        BattleReplayResult t_result = BattleReplay.Run(t_input!);
        ReplayResponse t_response = ReplayResponse.From(t_result);
        return t_result.Ok ? Results.Ok(t_response) : Results.UnprocessableEntity(t_response);
    }
    finally
    {
        SynergyRuleProvider.ResetScoped();
        MatchRandom.ResetScoped();
    }
});

app.Run();
