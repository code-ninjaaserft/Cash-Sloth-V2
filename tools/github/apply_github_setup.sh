#!/usr/bin/env bash
set -euo pipefail

if ! command -v gh >/dev/null 2>&1; then
  echo "gh is required but not found in PATH." >&2
  exit 1
fi

if ! command -v jq >/dev/null 2>&1; then
  echo "jq is required for the bash helper. Install jq or use apply_github_setup.ps1." >&2
  exit 1
fi

if ! gh auth status >/dev/null 2>&1; then
  echo "gh is not authenticated. Run 'gh auth login' first." >&2
  exit 1
fi

repo=$(gh repo view --json nameWithOwner | jq -r '.nameWithOwner')
root_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)

labels_file="$root_dir/tools/github/labels.json"
milestones_file="$root_dir/tools/github/milestones.json"
issues_qen_file="$root_dir/tools/github/issues_qen_gv.json"
issues_aug_file="$root_dir/tools/github/issues_august.json"

echo "=== Sync labels ==="
jq -c '.[]' "$labels_file" | while read -r label; do
  name=$(echo "$label" | jq -r '.name')
  color=$(echo "$label" | jq -r '.color')
  description=$(echo "$label" | jq -r '.description')
  gh label create "$name" --repo "$repo" --color "$color" --description "$description" --force >/dev/null
  echo "Upserted label: $name"
done

echo "=== Sync milestones ==="
existing_milestones=$(gh api "repos/$repo/milestones?state=all&per_page=100")
jq -c '.[]' "$milestones_file" | while read -r ms; do
  title=$(echo "$ms" | jq -r '.title')
  description=$(echo "$ms" | jq -r '.description')
  due_on=$(echo "$ms" | jq -r '.due_on')
  state=$(echo "$ms" | jq -r '.state')
  number=$(echo "$existing_milestones" | jq -r --arg title "$title" '.[] | select(.title == $title) | .number')
  if [[ -n "$number" ]]; then
    gh api -X PATCH "repos/$repo/milestones/$number" -f title="$title" -f description="$description" -f due_on="$due_on" -f state="$state" >/dev/null
    echo "Updated milestone: $title"
  else
    gh api -X POST "repos/$repo/milestones" -f title="$title" -f description="$description" -f due_on="$due_on" -f state="$state" >/dev/null
    echo "Created milestone: $title"
  fi
done

echo "=== Sync issues ==="
existing_milestones=$(gh api "repos/$repo/milestones?state=all&per_page=100")
existing_issues=$(gh api "repos/$repo/issues?state=all&per_page=100")
for file in "$issues_qen_file" "$issues_aug_file"; do
  jq -c '.[]' "$file" | while read -r issue; do
    title=$(echo "$issue" | jq -r '.title')
    body=$(echo "$issue" | jq -r '.body')
    milestone=$(echo "$issue" | jq -r '.milestone')
    labels=$(echo "$issue" | jq -r '.labels[]')
    milestone_number=$(echo "$existing_milestones" | jq -r --arg title "$milestone" '.[] | select(.title == $title) | .number')
    if [[ -z "$milestone_number" || "$milestone_number" == "null" ]]; then
      echo "Milestone '$milestone' not found in GitHub for repo $repo." >&2
      exit 1
    fi
    number=$(echo "$existing_issues" | jq -r --arg title "$title" '.[] | select(.title == $title) | .number')
    if [[ -n "$number" ]]; then
      args=(issue edit "$number" --repo "$repo" --title "$title" --body "$body" --milestone "$milestone_number")
      while read -r label; do
        args+=(--add-label "$label")
      done <<< "$labels"
      gh "${args[@]}" >/dev/null
      echo "Updated issue: $title"
    else
      args=(issue create --repo "$repo" --title "$title" --body "$body" --milestone "$milestone_number")
      while read -r label; do
        args+=(--label "$label")
      done <<< "$labels"
      gh "${args[@]}" >/dev/null
      echo "Created issue: $title"
    fi
  done
done
