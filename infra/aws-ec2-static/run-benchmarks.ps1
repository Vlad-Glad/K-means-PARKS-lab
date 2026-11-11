<#
.SYNOPSIS
    Automated benchmark runner for K-Means Parallel Image Segmentation on PARCS-NET.
#>
param(
    [Parameter(Mandatory = $false)]
    [string]$HostApiUrl = "http://localhost:5000",

    [Parameter(Mandatory = $true)]
    [string]$ImagePath,

    [Parameter(Mandatory = $false)]
    [int]$ClustersK = 8,

    [Parameter(Mandatory = $false)]
    [int]$MaxIterations = 40,

    [Parameter(Mandatory = $false)]
    [double]$ConvergenceThreshold = 0.5,

    [Parameter(Mandatory = $false)]
    [bool]$UseSpatialFeatures = $true,

    [Parameter(Mandatory = $false)]
    [double]$SpatialWeight = 0.35,

    [Parameter(Mandatory = $false)]
    [int]$Seed = 42,

    [Parameter(Mandatory = $false)]
    [int[]]$WorkerCounts = @(1, 2, 4, 8)
)

$ErrorActionPreference = "Stop"
$HostApiUrl = $HostApiUrl.TrimEnd('/')
$PublishDir = Resolve-Path "$PSScriptRoot\..\..\kmeans-module-publish"
$ResolvedImage = Resolve-Path $ImagePath
$ResultsDir = Join-Path $PSScriptRoot "benchmark-results"
New-Item -ItemType Directory -Force -Path $ResultsDir | Out-Null

Add-Type -AssemblyName System.Net.Http
$httpClient = [System.Net.Http.HttpClient]::new()
$httpClient.Timeout = [TimeSpan]::FromMinutes(15)

Write-Host "=== Uploading module Parcs.Modules.KMeansSegmentation to $HostApiUrl ===" -ForegroundColor Cyan
$moduleForm = [System.Net.Http.MultipartFormDataContent]::new()
$moduleForm.Add([System.Net.Http.StringContent]::new("KMeansSegmentation-" + (Get-Date -Format "HHmmss")), "Name")

foreach ($file in Get-ChildItem -Path $PublishDir -File) {
    $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
    $byteContent = [System.Net.Http.ByteArrayContent]::new($bytes)
    $moduleForm.Add($byteContent, "BinaryFiles", $file.Name)
}

$modResp = $httpClient.PostAsync("$HostApiUrl/api/Modules", $moduleForm).GetAwaiter().GetResult()
$modBody = $modResp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
if (-not $modResp.IsSuccessStatusCode) {
    throw "Failed to upload module: $($modResp.StatusCode) $modBody"
}
$moduleId = ($modBody | ConvertFrom-Json).moduleId
if (-not $moduleId) {
    throw "Module upload response is missing moduleId: $modBody"
}
Write-Host "Uploaded Module ID = $moduleId" -ForegroundColor Green

function Invoke-KMeansJob {
    param(
        [string]$Label,
        [string]$ClassName,
        [int]$PointsNumber
    )

    Write-Host "`n--- Running $Label (PointsNumber=$PointsNumber) ---" -ForegroundColor Yellow

    $jobForm = [System.Net.Http.MultipartFormDataContent]::new()
    $jobForm.Add([System.Net.Http.StringContent]::new("$moduleId"), "ModuleId")
    $jobForm.Add([System.Net.Http.StringContent]::new("Parcs.Modules.KMeansSegmentation"), "AssemblyName")
    $jobForm.Add([System.Net.Http.StringContent]::new($ClassName), "ClassName")
    $jobForm.Add([System.Net.Http.StringContent]::new("false"), "RequiresGpu")

    $imgBytes = [System.IO.File]::ReadAllBytes($ResolvedImage.Path)
    $imgContent = [System.Net.Http.ByteArrayContent]::new($imgBytes)
    $jobForm.Add($imgContent, "InputFiles", [System.IO.Path]::GetFileName($ResolvedImage.Path))

    $jobResp = $httpClient.PostAsync("$HostApiUrl/api/Jobs", $jobForm).GetAwaiter().GetResult()
    $jobBody = $jobResp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    if (-not $jobResp.IsSuccessStatusCode) {
        throw "Failed to create job: $($jobResp.StatusCode) $jobBody"
    }
    $jobId = ($jobBody | ConvertFrom-Json).jobId

    $runPayload = @{
        jobId = $jobId
        arguments = @{
            PointsNumber         = "$PointsNumber"
            ClustersK            = "$ClustersK"
            MaxIterations        = "$MaxIterations"
            ConvergenceThreshold = "$ConvergenceThreshold"
            UseSpatialFeatures   = $UseSpatialFeatures.ToString().ToLowerInvariant()
            SpatialWeight        = "$SpatialWeight"
            Seed                 = "$Seed"
        }
    } | ConvertTo-Json -Depth 5

    $jsonContent = [System.Net.Http.StringContent]::new($runPayload, [System.Text.Encoding]::UTF8, "application/json")
    $runResp = $httpClient.PostAsync("$HostApiUrl/api/SynchronousJobRuns", $jsonContent).GetAwaiter().GetResult()
    $runBody = $runResp.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    if (-not $runResp.IsSuccessStatusCode) {
        throw "Failed to run job $jobId : $($runResp.StatusCode) $runBody"
    }

    $outZipPath = Join-Path $ResultsDir "job_${jobId}_${Label}.zip"
    $extractDir = Join-Path $ResultsDir "job_${jobId}_${Label}"
    $outBytes = $httpClient.GetByteArrayAsync("$HostApiUrl/api/JobOutputs/$jobId").GetAwaiter().GetResult()
    [System.IO.File]::WriteAllBytes($outZipPath, $outBytes)

    if (Test-Path $extractDir) { Remove-Item $extractDir -Recurse -Force }
    Expand-Archive -Path $outZipPath -DestinationPath $extractDir -Force

    $metricsPath = Join-Path $extractDir "Output.txt"
    $metrics = Get-Content $metricsPath -Raw | ConvertFrom-Json
    $metrics | Add-Member -NotePropertyName "Label" -NotePropertyValue $Label -Force
    Write-Host ("Completed in {0:F4}s (Iterations={1})" -f $metrics.ElapsedSeconds, $metrics.IterationsPerformed) -ForegroundColor Green

    return $metrics
}

$allMetrics = @()

$seqMetrics = Invoke-KMeansJob `
    -Label "Sequential" `
    -ClassName "Parcs.Modules.KMeansSegmentation.Sequential.SequentialKMeansMainModule" `
    -PointsNumber 1
$allMetrics += $seqMetrics

foreach ($p in $WorkerCounts) {
    $parMetrics = Invoke-KMeansJob `
        -Label "Parallel_${p}w" `
        -ClassName "Parcs.Modules.KMeansSegmentation.Parallel.ParallelKMeansMainModule" `
        -PointsNumber $p
    $allMetrics += $parMetrics
}

Write-Host "`n==========================================================================" -ForegroundColor Cyan
$tSeq = $seqMetrics.ElapsedSeconds
Write-Host ("{0,-16} | {1,7} | {2,10} | {3,11} | {4,11}" -f "Mode", "Workers", "Time (s)", "Speedup S_p", "Effic. E_p")
Write-Host "-----------------+---------+------------+-------------+------------"

foreach ($m in $allMetrics) {
    $speedup = if ($m.ElapsedSeconds -gt 0) { $tSeq / $m.ElapsedSeconds } else { 0 }
    $efficiency = if ($m.PointsNumber -gt 0) { $speedup / $m.PointsNumber } else { 0 }
    Write-Host ("{0,-16} | {1,7} | {2,10:F4} | {3,10:F3}x | {4,10:P1}" -f `
        $m.Label, $m.PointsNumber, $m.ElapsedSeconds, $speedup, $efficiency)
}
Write-Host "==========================================================================" -ForegroundColor Cyan
