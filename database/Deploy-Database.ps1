param(
    [Parameter(Mandatory = $true)]
    [string]$ConnectionString,
    [string]$AppliedBy = $env:USERNAME,
    [string]$BackupDirectory = ".\backups"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.Data

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$versionDirectory = Join-Path $scriptRoot "versions"
$verifyFile = Join-Path $scriptRoot "verify\Verify.sql"
$logDirectory = Join-Path $scriptRoot "logs"
$deploymentReference = "DEP-{0}-{1}" -f (Get-Date -Format "yyyyMMdd"), (Get-Random -Maximum 10000).ToString("D4")

New-Item -ItemType Directory -Force -Path $logDirectory | Out-Null
$logFile = Join-Path $logDirectory "$deploymentReference.log"
Start-Transcript -Path $logFile -Force | Out-Null

function Write-Step {
    param([string]$Message)

    Write-Host "[$deploymentReference] $Message"
}

function Invoke-SqlBatches {
    param(
        [System.Data.SqlClient.SqlConnection]$Connection,
        [string]$Sql,
        [System.Data.SqlClient.SqlTransaction]$Transaction = $null
    )

    $batches = [regex]::Split($Sql, "(?im)^\s*GO\s*(?:--.*)?$") |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    foreach ($batch in $batches) {
        $command = $Connection.CreateCommand()
        $command.CommandTimeout = 0
        $command.CommandText = $batch
        $command.Transaction = $Transaction
        [void]$command.ExecuteNonQuery()
    }
}

$connection = [System.Data.SqlClient.SqlConnection]::new($ConnectionString)

try {
    Write-Step "Starting database deployment."
    $connection.Open()

    $databaseNameCommand = $connection.CreateCommand()
    $databaseNameCommand.CommandText = "SELECT DB_NAME();"
    $databaseName = [string]$databaseNameCommand.ExecuteScalar()

    New-Item -ItemType Directory -Force -Path $BackupDirectory | Out-Null
    $backupFile = Join-Path (Resolve-Path $BackupDirectory).Path (
        "{0}_predeploy_{1}.bak" -f $databaseName, (Get-Date -Format "yyyyMMdd_HHmmss")
    )
    $safeDatabaseName = $databaseName.Replace("]", "]]")
    $safeBackupFile = $backupFile.Replace("'", "''")
    $backup = $connection.CreateCommand()
    $backup.CommandTimeout = 0
    $backup.CommandText = "BACKUP DATABASE [$safeDatabaseName] TO DISK = N'$safeBackupFile' WITH COPY_ONLY, INIT, CHECKSUM;"
    Write-Step "Creating backup: $backupFile"
    [void]$backup.ExecuteNonQuery()
    Write-Step "Backup completed."

    $bootstrap = $connection.CreateCommand()
    $bootstrap.CommandText = @"
IF SCHEMA_ID(N'system') IS NULL
    EXEC(N'CREATE SCHEMA system AUTHORIZATION dbo;');

IF OBJECT_ID(N'system.DatabaseVersions', N'U') IS NULL
BEGIN
    CREATE TABLE system.DatabaseVersions
    (
        Version int NOT NULL CONSTRAINT PK_DatabaseVersions PRIMARY KEY,
        VersionName nvarchar(200) NOT NULL,
        Checksum char(64) NOT NULL,
        AppliedAtUtc datetime2(3) NOT NULL DEFAULT SYSUTCDATETIME(),
        AppliedBy nvarchar(200) NOT NULL,
        ExecutionMs bigint NOT NULL,
        CONSTRAINT UQ_DatabaseVersions_Checksum UNIQUE (Checksum)
    );
END;
"@
    [void]$bootstrap.ExecuteNonQuery()

    $appliedVersions = @{}
    $query = $connection.CreateCommand()
    $query.CommandText = "SELECT Version, Checksum FROM system.DatabaseVersions;"
    $reader = $query.ExecuteReader()
    while ($reader.Read()) {
        $appliedVersions[[int]$reader["Version"]] = [string]$reader["Checksum"]
    }
    $reader.Close()

    $versionFiles = Get-ChildItem -Path $versionDirectory -Filter "V????__*.sql" | Sort-Object Name
    $appliedCount = 0

    foreach ($file in $versionFiles) {
        if ($file.Name -notmatch "^V(?<Version>\d{4})__(?<Name>.+)\.sql$") {
            continue
        }

        $version = [int]$Matches["Version"]
        $versionName = $Matches["Name"].Replace("_", " ")
        $checksum = (Get-FileHash -Path $file.FullName -Algorithm SHA256).Hash.ToUpperInvariant()

        if ($appliedVersions.ContainsKey($version)) {
            if ($appliedVersions[$version] -ne $checksum) {
                throw "V$($version.ToString('D4')) checksum mismatch. Applied versions are immutable."
            }

            Write-Step "V$($version.ToString('D4')) already applied."
            continue
        }

        Write-Step "Applying V$($version.ToString('D4')) - $versionName."
        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        $transaction = $connection.BeginTransaction()
        try {
            $sql = Get-Content -Raw -Encoding UTF8 $file.FullName
            Invoke-SqlBatches -Connection $connection -Sql $sql -Transaction $transaction
            $stopwatch.Stop()

            $insert = $connection.CreateCommand()
            $insert.Transaction = $transaction
            $insert.CommandText = @"
INSERT system.DatabaseVersions
    (Version, VersionName, Checksum, AppliedBy, ExecutionMs)
VALUES
    (@Version, @VersionName, @Checksum, @AppliedBy, @ExecutionMs);
"@
            [void]$insert.Parameters.Add("@Version", [System.Data.SqlDbType]::Int)
            [void]$insert.Parameters.Add("@VersionName", [System.Data.SqlDbType]::NVarChar, 200)
            [void]$insert.Parameters.Add("@Checksum", [System.Data.SqlDbType]::Char, 64)
            [void]$insert.Parameters.Add("@AppliedBy", [System.Data.SqlDbType]::NVarChar, 200)
            [void]$insert.Parameters.Add("@ExecutionMs", [System.Data.SqlDbType]::BigInt)
            $insert.Parameters["@Version"].Value = $version
            $insert.Parameters["@VersionName"].Value = $versionName
            $insert.Parameters["@Checksum"].Value = $checksum
            $insert.Parameters["@AppliedBy"].Value = $AppliedBy
            $insert.Parameters["@ExecutionMs"].Value = $stopwatch.ElapsedMilliseconds
            [void]$insert.ExecuteNonQuery()
            $transaction.Commit()
            $appliedCount++
        }
        catch {
            try {
                $transaction.Rollback()
            }
            catch {
                Write-Step "Transaction rollback also failed."
            }
            throw
        }
        finally {
            $transaction.Dispose()
        }
    }

    Write-Step "Running Verify.sql."
    Invoke-SqlBatches -Connection $connection -Sql (Get-Content -Raw -Encoding UTF8 $verifyFile)
    Write-Step "SUCCESS. Applied $appliedCount pending version(s). Log: $logFile"
}
catch {
    Write-Step "FAILED: $($_.Exception.Message)"
    throw
}
finally {
    $connection.Dispose()
    Stop-Transcript | Out-Null
}
