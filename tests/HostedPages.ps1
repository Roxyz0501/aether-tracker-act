$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
$asm = [Reflection.Assembly]::LoadFrom((Resolve-Path "$PSScriptRoot/../dist/CharacterTracker.dll"))
$settings = [Activator]::CreateInstance($asm.GetType('CharacterTracker.TrackerSettings'))
$settings.Language = 'ja'
$type = $asm.GetType('CharacterTracker.PluginControl')
$ui = [Activator]::CreateInstance($type, @($settings))
try {
    $tabs = $type.GetField('tabs', 'NonPublic,Instance').GetValue($ui)
    if ($tabs.TabCount -ne 6) { throw 'Standalone pages missing' }
    $tabs.AddContentPage([Windows.Forms.TabPage]::new('Extra'))
    if ($tabs.TabPages[6] -ne $type.GetField('supportPage','NonPublic,Instance').GetValue($ui)) { throw 'Support is not last' }
    foreach ($lang in 'ja','en','zh-CN','ko','ja','ja') {
        $ui.SetHostedLanguage($lang)
        $effective = $asm.GetType('CharacterTracker.Localization').GetProperty('Language').GetValue($null)
        $expected = if ($lang -eq 'zh-CN') { 'zh-Hans' } else { $lang }
        if ($effective -ne $expected -or $settings.Language -ne 'ja' -or $tabs.TabCount -ne 4) { throw "Hosted language: $lang" }
        $ui.Size = [Drawing.Size]::new(360,480)
        $bmp = [Drawing.Bitmap]::new(360,480)
        try { $ui.DrawToBitmap($bmp, $ui.ClientRectangle) } finally { $bmp.Dispose() }
    }
} finally { $ui.Dispose() }
$ui = [Activator]::CreateInstance($type, @($settings))
try { if ($type.GetField('languages','NonPublic,Instance').GetValue($ui).LanguageCode -ne 'ja') { throw 'Standalone language not restored' } } finally { $ui.Dispose() }
'Hosted and standalone checks passed.'
