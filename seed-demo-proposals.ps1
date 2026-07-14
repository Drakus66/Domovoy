# SPDX-License-Identifier: AGPL-3.0-or-later
# Copyright (C) 2025-2026 Ilya Dryagin
#
# Наполняет очередь «Предложения» (/proposals) тестовыми карточками всех четырёх видов,
# чтобы посмотреть страницу в работе, не дожидаясь, пока сканеры найдут что-то в истории.
# Все карточки помечены источником «demo» — их можно смело отклонять; «Одобрить» выполняет
# настоящий эффект (правило включится, стадия блока изменится и т.д.), это честный прогон 2C.
#
# Запуск (система должна работать): .\seed-demo-proposals.ps1 [-Gateway http://localhost:5000]

param([string]$Gateway = 'http://localhost:5000')

$ErrorActionPreference = 'Stop'

function Post-Json([string]$Path, $Body) {
    $json = $Body | ConvertTo-Json -Depth 10
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    return Invoke-RestMethod -Method Post -Uri "$Gateway$Path" -ContentType 'application/json; charset=utf-8' -Body $bytes
}

# Invoke-RestMethod (PS 5.1) может вернуть JSON-массив одним Object[] — разворачиваем через конвейер.
function Get-Json([string]$Path) {
    return @(Invoke-RestMethod -Uri "$Gateway$Path" | ForEach-Object { $_ })
}

Write-Host "Читаю текущее состояние из $Gateway ..."
$devices = Get-Json '/api/capability-devices'
$blocks  = Get-Json '/api/blocks'
$tasks   = Get-Json '/api/ml/tasks'
$models  = Get-Json '/api/ml/models'
$open    = Get-Json '/api/proposals?status=Proposed'

$created = 0
function New-DemoProposal($Proposal) {
    if ($open | Where-Object { $_.title -eq $Proposal.title }) {
        Write-Host "  пропуск (уже в очереди): $($Proposal.title)"
        return
    }
    Post-Json '/api/proposals' $Proposal | Out-Null
    Write-Host "  создано: $($Proposal.title)"
    $script:created++
}

# --- 1. Правило: датчик-триггер -> включить исполнитель (как нашёл бы сканер журнала) ---
$triggerCaps = @('motion', 'occupancy', 'presence', 'contact', 'is_dark')
$trigger = $null; $triggerCap = $null
foreach ($d in $devices) {
    $cap = $d.capabilities | Where-Object { $triggerCaps -contains $_.id } | Select-Object -First 1
    if ($cap) { $trigger = $d; $triggerCap = $cap.id; break }
}
$actuator = $devices |
    Where-Object { $_.id -ne $trigger.id -and ($_.capabilities | Where-Object { $_.id -eq 'on_off' -and $_.writable }) } |
    Select-Object -First 1

if ($trigger -and $actuator) {
    Write-Host "Правило: $($trigger.name) ($triggerCap) -> $($actuator.name)"
    $rule = Post-Json '/api/automations' @{
        name        = "Демо: включать «$($actuator.name)» по $triggerCap («$($trigger.name)»)"
        description = 'Демо-кандидат для страницы «Предложения». До одобрения не выполняется.'
        status      = 'Proposed'
        triggers    = @(@{ type = 'DeviceState'; deviceId = $trigger.id; capabilityId = $triggerCap; operator = 'eq'; value = $true })
        conditions  = @()
        actions     = @(@{ type = 'Command'; deviceId = $actuator.id; set = @{ on_off = $true } })
    }
    New-DemoProposal @{
        kind     = 'Rule'
        title    = "Демо: включать «$($actuator.name)» по $triggerCap («$($trigger.name)»)"
        source   = 'demo'
        ruleId   = $rule.id
        evidence = @{ support = 14; confidence = 0.86; windowDays = 7 }
    }
}
else {
    Write-Warning 'Правило пропущено: не нашлось пары «датчик + управляемое устройство on_off».'
}

# --- 2. Повышение полномочий ML-блока (как запросили бы вы со страницы «Блоки») ---
$governor = $blocks | Where-Object { $_.params.PSObject.Properties.Name -contains 'stage' } | Select-Object -First 1
if ($governor) {
    $from = [int]$governor.params.stage
    $to = [Math]::Min(2, $from + 1)
    New-DemoProposal @{
        kind      = 'BlockPromotion'
        title     = "Демо: повысить полномочия блока «$($governor.name)»"
        rationale = 'Тестовая карточка: посмотрите стадии «было -> станет» ниже.'
        source    = 'demo'
        blockId   = $governor.id
        fromStage = $from
        toStage   = $to
    }

    # --- 3. Закрепление версии модели за тем же блоком ---
    $version = 1
    $model = $models | Sort-Object version -Descending | Select-Object -First 1
    if ($model) { $version = [int]$model.version }
    New-DemoProposal @{
        kind         = 'ModelSelection'
        title        = "Демо: закрепить модель v$version за блоком «$($governor.name)»"
        rationale    = 'Тестовая карточка: блок перестанет переключаться на новые версии.'
        source       = 'demo'
        blockId      = $governor.id
        modelVersion = $version
    }
}
else {
    Write-Warning 'Повышение/закрепление пропущено: в доме нет ML-блока (блока с параметром stage).'
}

# --- 4. Задача обучения (как предложил бы сканер ML-задач, Epic 2P) ---
$known = @($tasks | ForEach-Object { $_.targetCapability.ToLowerInvariant() })
$mlCandidates = @('humidity', 'illuminance', 'co2', 'pressure', 'temperature')
$mlTarget = $mlCandidates | Where-Object { $known -notcontains $_ } | Select-Object -First 1
if ($mlTarget) {
    New-DemoProposal @{
        kind         = 'MlTask'
        title        = "Демо: начать обучение «$mlTarget»"
        source       = 'demo'
        mlTaskTarget = $mlTarget
        evidence     = @{ samples = 480; required = 20; windowDays = 30 }
    }
}
else {
    Write-Warning 'ML-задача пропущена: по всем типовым величинам задачи уже существуют.'
}

Write-Host ''
Write-Host "Готово: создано карточек — $created. Откройте страницу «Предложения» (/proposals)."
Write-Host 'Карточки с источником «demo» можно смело отклонять; «Одобрить» применит настоящий эффект.'
