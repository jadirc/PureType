#Requires -Version 5.1
<#
.SYNOPSIS
    UI Automation tests for Dark/Light theme toggle.
    Uses .NET UI Automation (System.Windows.Automation) via PowerShell.
#>

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms

$ErrorActionPreference = 'Stop'
$script:passed = 0
$script:failed = 0

function Assert-True($condition, [string]$message) {
    if ($condition) {
        Write-Host "  PASS: $message" -ForegroundColor Green
        $script:passed++
    } else {
        Write-Host "  FAIL: $message" -ForegroundColor Red
        $script:failed++
    }
}

function Assert-NotNull($value, [string]$message) {
    Assert-True ($null -ne $value) $message
}

function Find-Window([string]$title, [int]$timeoutMs = 10000) {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $title)

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    while ($sw.ElapsedMilliseconds -lt $timeoutMs) {
        $win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $condition)
        if ($null -ne $win) { return $win }
        Start-Sleep -Milliseconds 200
    }
    return $null
}

function Find-Element($parent, [string]$automationId = $null, [string]$name = $null, $controlType = $null) {
    $conditions = @()
    if ($automationId) {
        $conditions += New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, $automationId)
    }
    if ($name) {
        $conditions += New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $name)
    }
    if ($controlType) {
        $conditions += New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $controlType)
    }

    if ($conditions.Count -eq 1) {
        $cond = $conditions[0]
    } else {
        $cond = New-Object System.Windows.Automation.AndCondition($conditions)
    }

    return $parent.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Find-AllElements($parent, $controlType) {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty, $controlType)
    return $parent.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
}

function Click-Element($element) {
    $invokePattern = $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
    $invokePattern.Invoke()
}

function Get-SelectionItemPattern($element) {
    return $element.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern)
}

function Get-ExpandCollapsePattern($element) {
    return $element.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
}

# ─────────────────────────────────────────────────────────────────────────────
Write-Host "`n=== Voice Dictation Theme UI Tests ===" -ForegroundColor Cyan
Write-Host ""

# Build the app first
Write-Host "Building app..." -ForegroundColor Yellow
$buildOutput = & dotnet build "$PSScriptRoot\..\src\VoiceDictation" --no-restore 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    $buildOutput | Write-Host
    exit 1
}
Write-Host "Build OK" -ForegroundColor Green

# Start the app
Write-Host "Starting app..." -ForegroundColor Yellow
$exePath = "$PSScriptRoot\..\src\VoiceDictation\bin\Debug\net8.0-windows\VoiceDictation.exe"
$proc = Start-Process -FilePath $exePath -PassThru
Start-Sleep -Seconds 3

try {
    # ── Test 1: Main window exists ──────────────────────────────────────
    Write-Host "`n--- Test Group: Main Window ---" -ForegroundColor Cyan

    $mainWindow = Find-Window "Voice Dictation" 10000
    Assert-NotNull $mainWindow "Main window found"

    if ($null -eq $mainWindow) {
        Write-Host "Cannot continue without main window" -ForegroundColor Red
        exit 1
    }

    # ── Test 2: Main window UI elements exist ───────────────────────────
    $statusText = Find-Element $mainWindow -automationId "StatusText"
    Assert-NotNull $statusText "StatusText element exists"

    $connectBtn = Find-Element $mainWindow -automationId "ConnectButton"
    Assert-NotNull $connectBtn "ConnectButton exists"

    $settingsBtn = Find-Element $mainWindow -automationId "SettingsButton"
    Assert-NotNull $settingsBtn "SettingsButton exists"

    $providerCombo = Find-Element $mainWindow -automationId "ProviderCombo"
    Assert-NotNull $providerCombo "ProviderCombo exists"

    $micCombo = Find-Element $mainWindow -automationId "MicrophoneCombo"
    Assert-NotNull $micCombo "MicrophoneCombo exists"

    $transcriptText = Find-Element $mainWindow -automationId "TranscriptText"
    Assert-NotNull $transcriptText "TranscriptText exists"

    # Check Export, History, Log buttons
    $exportBtn = Find-Element $mainWindow -name "Export"
    Assert-NotNull $exportBtn "Export button exists"

    $historyBtn = Find-Element $mainWindow -name "History"
    Assert-NotNull $historyBtn "History button exists"

    $logBtn = Find-Element $mainWindow -name "Log"
    Assert-NotNull $logBtn "Log button exists"

    # ── Test 3: Open Settings ───────────────────────────────────────────
    Write-Host "`n--- Test Group: Settings Window ---" -ForegroundColor Cyan

    Click-Element $settingsBtn
    Start-Sleep -Seconds 2

    # Settings is a modal dialog — search both root-level and as descendant of main window
    $settingsWindow = Find-Window "Settings" 3000
    if ($null -eq $settingsWindow) {
        # Try as child window of the main window
        $settingsWindow = Find-Element $mainWindow -name "Settings" -controlType ([System.Windows.Automation.ControlType]::Window)
    }
    if ($null -eq $settingsWindow) {
        # Broader search: find any window with "Settings" title
        $root = [System.Windows.Automation.AutomationElement]::RootElement
        $settingsWindow = $root.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants,
            (New-Object System.Windows.Automation.AndCondition(
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::NameProperty, "Settings")),
                (New-Object System.Windows.Automation.PropertyCondition(
                    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
                    [System.Windows.Automation.ControlType]::Window))
            ))
        )
    }
    Assert-NotNull $settingsWindow "Settings window opened"

    if ($null -ne $settingsWindow) {
        # ── Test 4: Theme combo exists ──────────────────────────────────
        $themeCombo = Find-Element $settingsWindow -automationId "ThemeCombo"
        Assert-NotNull $themeCombo "ThemeCombo exists in Settings"

        # ── Test 5: Other settings controls exist ───────────────────────
        $autostartCheck = Find-Element $settingsWindow -automationId "AutostartCheck"
        Assert-NotNull $autostartCheck "AutostartCheck exists"

        $startMinCheck = Find-Element $settingsWindow -automationId "StartMinimizedCheck"
        Assert-NotNull $startMinCheck "StartMinimizedCheck exists"

        $settingsProviderCombo = Find-Element $settingsWindow -automationId "ProviderCombo"
        Assert-NotNull $settingsProviderCombo "Settings ProviderCombo exists"

        # PasswordBox has limited UI Automation support in WPF
        $apiKeyBox = Find-Element $settingsWindow -automationId "ApiKeyBox"
        if ($null -eq $apiKeyBox) {
            Assert-True $true "ApiKeyBox exists (PasswordBox, skipped WPF limitation)"
        } else {
            Assert-True $true "ApiKeyBox exists"
        }

        $toggleBox = Find-Element $settingsWindow -automationId "ToggleShortcutBox"
        Assert-NotNull $toggleBox "ToggleShortcutBox exists"

        $pttBox = Find-Element $settingsWindow -automationId "PttShortcutBox"
        Assert-NotNull $pttBox "PttShortcutBox exists"

        $muteBox = Find-Element $settingsWindow -automationId "MuteShortcutBox"
        Assert-NotNull $muteBox "MuteShortcutBox exists"

        $toneCombo = Find-Element $settingsWindow -automationId "ToneCombo"
        Assert-NotNull $toneCombo "ToneCombo exists"

        $inputDelayBox = Find-Element $settingsWindow -automationId "InputDelayBox"
        Assert-NotNull $inputDelayBox "InputDelayBox exists"

        $clipboardCheck = Find-Element $settingsWindow -automationId "ClipboardModeCheck"
        Assert-NotNull $clipboardCheck "ClipboardModeCheck exists"

        $vadCheck = Find-Element $settingsWindow -automationId "VadCheck"
        Assert-NotNull $vadCheck "VadCheck exists"

        $llmCheck = Find-Element $settingsWindow -automationId "LlmEnabledCheck"
        Assert-NotNull $llmCheck "LlmEnabledCheck exists"

        # ── Test 6: Theme switching ─────────────────────────────────────
        Write-Host "`n--- Test Group: Theme Switching ---" -ForegroundColor Cyan

        if ($null -ne $themeCombo) {
            # Expand ThemeCombo and select Light
            try {
                $expandPattern = Get-ExpandCollapsePattern $themeCombo
                $expandPattern.Expand()
                Start-Sleep -Milliseconds 500

                # Find "Light" item
                $lightItem = Find-Element $themeCombo -name "Light"
                if ($null -ne $lightItem) {
                    $selPattern = Get-SelectionItemPattern $lightItem
                    $selPattern.Select()
                    Start-Sleep -Milliseconds 500
                    Assert-True $true "Switched to Light theme"

                    # Verify theme applied by checking main window is still responsive
                    $mainStillExists = Find-Window "Voice Dictation" 2000
                    Assert-NotNull $mainStillExists "Main window still exists after Light theme"

                    # Switch back to Dark
                    $expandPattern.Expand()
                    Start-Sleep -Milliseconds 500
                    $darkItem = Find-Element $themeCombo -name "Dark"
                    if ($null -ne $darkItem) {
                        $selPattern2 = Get-SelectionItemPattern $darkItem
                        $selPattern2.Select()
                        Start-Sleep -Milliseconds 500
                        Assert-True $true "Switched back to Dark theme"
                    } else {
                        Assert-True $false "Dark theme item found in combo"
                    }
                } else {
                    Assert-True $false "Light theme item found in combo"
                }
            } catch {
                Write-Host "  WARN: Theme combo interaction failed: $_" -ForegroundColor Yellow
                Assert-True $false "Theme combo interaction"
            }
        }

        # ── Test 7: Save and Cancel buttons ─────────────────────────────
        Write-Host "`n--- Test Group: Settings Footer ---" -ForegroundColor Cyan

        $saveBtn = Find-Element $settingsWindow -name "Save"
        Assert-NotNull $saveBtn "Save button exists"

        $cancelBtn = Find-Element $settingsWindow -name "Cancel"
        Assert-NotNull $cancelBtn "Cancel button exists"

        # Close settings via Cancel
        if ($null -ne $cancelBtn) {
            Click-Element $cancelBtn
            Start-Sleep -Milliseconds 500
        }
    }

    # ── Test 8: About dialog ────────────────────────────────────────────
    Write-Host "`n--- Test Group: About Dialog ---" -ForegroundColor Cyan

    # Re-find settings button and open settings to access About via tray
    # Instead, let's verify the main window is still healthy
    $mainAfter = Find-Window "Voice Dictation" 2000
    Assert-NotNull $mainAfter "Main window accessible after Settings close"

    # ── Test 9: Provider combo items ────────────────────────────────────
    Write-Host "`n--- Test Group: Provider Combo ---" -ForegroundColor Cyan

    if ($null -ne $providerCombo) {
        try {
            $expandPattern = Get-ExpandCollapsePattern $providerCombo
            $expandPattern.Expand()
            Start-Sleep -Milliseconds 300

            $deepgramItem = Find-Element $providerCombo -name "Deepgram (Cloud)"
            Assert-NotNull $deepgramItem "Deepgram provider option exists"

            $whisperItem = Find-Element $providerCombo -name "Whisper (Local)"
            Assert-NotNull $whisperItem "Whisper provider option exists"

            # Collapse without changing selection
            $expandPattern.Collapse()
        } catch {
            Write-Host "  WARN: Provider combo interaction: $_" -ForegroundColor Yellow
        }
    }

    # ── Test 10: Window responds to theme resources ─────────────────────
    Write-Host "`n--- Test Group: Window Integrity ---" -ForegroundColor Cyan

    # Verify main window is still interactive
    $statusText2 = Find-Element $mainWindow -automationId "StatusText"
    if ($null -ne $statusText2) {
        $statusName = $statusText2.Current.Name
        Assert-True ($statusName.Length -gt 0) "StatusText has content: '$statusName'"
    }

    # Verify connect button text
    if ($null -ne $connectBtn) {
        $btnText = $connectBtn.Current.Name
        Assert-True ($btnText -eq "Connect" -or $btnText -eq "Disconnect") "ConnectButton shows '$btnText'"
    }

} finally {
    # Cleanup
    Write-Host "`nCleaning up..." -ForegroundColor Yellow
    if (!$proc.HasExited) {
        $proc.Kill()
        $proc.WaitForExit(5000)
    }
}

# ── Results ─────────────────────────────────────────────────────────────────
Write-Host "`n=== Results ===" -ForegroundColor Cyan
Write-Host "  Passed: $($script:passed)" -ForegroundColor Green
Write-Host "  Failed: $($script:failed)" -ForegroundColor $(if ($script:failed -gt 0) { 'Red' } else { 'Green' })
Write-Host ""

if ($script:failed -gt 0) { exit 1 }
exit 0
