Param(
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

function Write-Section($title) {
    Write-Host "\n=== $title ===" -ForegroundColor Cyan
}

function Require-Gh {
    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw "GitHub CLI (gh) is required but was not found in PATH."
    }

    $authStatus = gh auth status 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "GitHub CLI is not authenticated. Run 'gh auth login' first."
    }
}

function Get-Repo {
    $repoJson = gh repo view --json nameWithOwner | ConvertFrom-Json
    if (-not $repoJson.nameWithOwner) {
        throw "Unable to infer repository from gh repo view."
    }
    return $repoJson.nameWithOwner
}

function Load-JsonFile($path) {
    if (-not (Test-Path $path)) {
        throw "Missing required file: $path"
    }
    return Get-Content $path -Raw | ConvertFrom-Json
}

function Invoke-Gh([string[]]$args, [string]$summary, [ref]$actions) {
    if ($DryRun) {
        $actions.Value += "[dry-run] $summary"
        return
    }

    & gh @args | Out-Null
    $actions.Value += $summary
}

Require-Gh

$repo = Get-Repo
$labelsPath = Join-Path $PSScriptRoot "labels.json"
$milestonesPath = Join-Path $PSScriptRoot "milestones.json"
$issuesQenPath = Join-Path $PSScriptRoot "issues_qen_gv.json"
$issuesAugustPath = Join-Path $PSScriptRoot "issues_august.json"

$labelActions = @()
$milestoneActions = @()
$issueActions = @()

Write-Section "Sync labels"
$labels = Load-JsonFile $labelsPath
foreach ($label in $labels) {
    $name = $label.name
    $color = $label.color
    $description = $label.description
    $args = @("label", "create", $name, "--repo", $repo, "--color", $color, "--description", $description, "--force")
    Invoke-Gh $args "Upserted label: $name" ([ref]$labelActions)
}

Write-Section "Sync milestones"
$milestones = Load-JsonFile $milestonesPath
$existingMilestones = gh api "repos/$repo/milestones?state=all&per_page=100" | ConvertFrom-Json
foreach ($milestone in $milestones) {
    $existing = $existingMilestones | Where-Object { $_.title -eq $milestone.title } | Select-Object -First 1
    $title = $milestone.title
    $description = $milestone.description
    $dueOn = $milestone.due_on
    $state = $milestone.state

    if ($existing) {
        $args = @(
            "api", "-X", "PATCH", "repos/$repo/milestones/$($existing.number)",
            "-f", "title=$title",
            "-f", "description=$description",
            "-f", "due_on=$dueOn",
            "-f", "state=$state"
        )
        Invoke-Gh $args "Updated milestone: $title" ([ref]$milestoneActions)
    } else {
        $args = @(
            "api", "-X", "POST", "repos/$repo/milestones",
            "-f", "title=$title",
            "-f", "description=$description",
            "-f", "due_on=$dueOn",
            "-f", "state=$state"
        )
        Invoke-Gh $args "Created milestone: $title" ([ref]$milestoneActions)
    }
}

Write-Section "Sync issues"
$existingIssues = gh api "repos/$repo/issues?state=all&per_page=100" | ConvertFrom-Json
$issueSets = @(
    Load-JsonFile $issuesQenPath,
    Load-JsonFile $issuesAugustPath
)

foreach ($issues in $issueSets) {
    foreach ($issue in $issues) {
        $title = $issue.title
        $body = $issue.body
        $labels = $issue.labels -join ","
        $milestone = $issue.milestone
        $tempBody = New-TemporaryFile
        Set-Content -Path $tempBody -Value $body -NoNewline

        $existing = $existingIssues | Where-Object { $_.title -eq $title } | Select-Object -First 1
        if ($existing) {
            $args = @(
                "issue", "edit", "$($existing.number)",
                "--repo", $repo,
                "--title", $title,
                "--body-file", $tempBody,
                "--milestone", $milestone,
                "--add-label", $labels
            )
            Invoke-Gh $args "Updated issue: $title" ([ref]$issueActions)
        } else {
            $args = @(
                "issue", "create",
                "--repo", $repo,
                "--title", $title,
                "--body-file", $tempBody,
                "--milestone", $milestone,
                "--label", $labels
            )
            Invoke-Gh $args "Created issue: $title" ([ref]$issueActions)
        }

        Remove-Item -Path $tempBody -Force
    }
}

Write-Section "Summary"
Write-Host "Labels: $($labelActions.Count) action(s)"
$labelActions | ForEach-Object { Write-Host " - $_" }
Write-Host "Milestones: $($milestoneActions.Count) action(s)"
$milestoneActions | ForEach-Object { Write-Host " - $_" }
Write-Host "Issues: $($issueActions.Count) action(s)"
$issueActions | ForEach-Object { Write-Host " - $_" }

Write-Host "\nRepository: $repo"
Write-Host "Dry run: $DryRun"
