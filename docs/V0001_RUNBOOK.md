# V0001 Runbook

## Scope
Foundation, centralized error/audit/operational logging, correlation IDs, database versioning, database deploy/verify and deployment transcript logging.

## Deployment
1. Create an empty SQL Server database (recommended name: CairoChatbotDB).
2. Run `database/Deploy-Database.ps1 -ConnectionString "..."` from PowerShell 7/Windows PowerShell.
3. The tool always creates a pre-deployment backup and stops if backup fails.
4. Applied version scripts are immutable; checksum mismatch blocks deployment.
5. Run the API with `ConnectionStrings__ChatbotDatabase` supplied as an environment secret.

## Logging guarantees
- Unexpected backend exceptions are logged through `audit.SystemError_Log` using a separate SQL connection after the failed operation unwinds.
- Frontend critical errors are sent to `/api/v1/system/client-errors` and logged as `FRONTEND`.
- Every HTTP request receives `X-Correlation-ID`; the same value is returned to the caller.
- Logging failure never replaces/hides the original exception.

## Stored procedures
Operational stored procedures in later versions must use TRY/CATCH + THROW and accept `@CorrelationId` plus actor context where relevant. Application-layer logging is authoritative to avoid rollback of the error record inside a failed business transaction.

## Mandatory stored-procedure error pattern for later versions
Every operational stored procedure must own or explicitly handle its transaction boundary. In `CATCH`, capture `ERROR_*()` values, roll back the failed business transaction first, call `audit.SystemError_Log` outside that transaction, then `THROW`. This keeps the error record even when business data is rolled back. The application logger is an additional safety net and preserves the same error reference when supplied.

## Validation status of this package
Static structure/content checks were run in the generation environment. A .NET SDK, PowerShell runtime, and SQL Server instance were not available there, so compilation and live SQL deployment must be performed in the target/dev environment before V0001 is marked production-accepted.
