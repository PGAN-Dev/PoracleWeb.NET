using Pgan.PoracleWebNet.Core.Abstractions.Services;

namespace Pgan.PoracleWebNet.Core.Services.TestAlerts;

/// <summary>
/// Claims the <c>nest</c> alarm type but refuses to build a payload. PoracleNG's
/// <c>/api/test</c> endpoint has no upstream example nest entry (neither <c>fallbacks/</c>
/// nor <c>config/testdata.json</c> on the live server ships one), nest webhooks arrive
/// from a separate ingestion path, and the server may reject or silently drop nest tests.
/// Rather than ship a guessed shape that looks like it worked when it didn't, we surface
/// a clear error the controller can translate into an HTTP 501 the frontend can explain.
/// </summary>
public sealed class NestTestPayloadBuilder : ITestPayloadBuilder
{
    public bool CanBuild(string alarmType) => alarmType == "nest";

    public Task<TestPayloadBuildResult> BuildAsync(TestPayloadContext context) =>
        throw new NotSupportedException(
            "Nest test alerts aren't supported by PoracleNG's /api/test endpoint. Nest webhooks ingest through a different path with no test surface.");
}
