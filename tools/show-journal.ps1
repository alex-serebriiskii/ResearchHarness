param(
    [Parameter(Mandatory=$true)]
    [string]$JobId,
    [int]$Port = 5000
)

$url = "http://localhost:$Port/internal/research/$JobId/journal"

try {
    $journal = Invoke-RestMethod -Uri $url -Method Get -ErrorAction Stop
} catch {
    Write-Host "Error fetching journal: $_"
    exit 1
}

Write-Host "=== OVERALL SUMMARY ==="
Write-Host $journal.overallSummary

Write-Host ""
Write-Host "=== CROSS-TOPIC ANALYSIS ==="
Write-Host $journal.crossTopicAnalysis

if ($journal.papers) {
    foreach ($paper in $journal.papers) {
        Write-Host ""
        Write-Host "=== PAPER: $($paper.topicId) ==="
        Write-Host "Executive Summary: $($paper.executiveSummary)"
        Write-Host "Confidence: $($paper.confidenceScore)"
        if ($paper.findings) {
            $n = 1
            foreach ($finding in $paper.findings) {
                Write-Host "  $n. [$($finding.subTopic)] $($finding.summary)"
                $n++
            }
        }
    }
}

if ($journal.masterBibliography) {
    $count = $journal.masterBibliography.Count
    Write-Host ""
    Write-Host "=== BIBLIOGRAPHY ($count sources) ==="
    foreach ($source in $journal.masterBibliography) {
        Write-Host "  [$($source.credibility)] $($source.title) | $($source.url)"
    }
}

exit 0
