param(
    [string] $ProfilePath = (Join-Path $env:APPDATA '3Dconnexion/3DxWare/Cfg/Danslicer.App.xml')
)
$ErrorActionPreference = 'Stop'

# Only this app's profile is changed. An existing profile is mandatory so customized device
# and button settings are preserved, rather than replacing them with a generic profile.
$resolvedPath = (Resolve-Path -LiteralPath $ProfilePath).Path
[xml] $profile = Get-Content -LiteralPath $resolvedPath -Raw
if ($profile.DocumentElement.Name -ne 'AppCfg' -or
    $profile.SelectSingleNode('/AppCfg/AppInfo/Signature/ExecutableName').InnerText -ine 'Danslicer.App.exe') {
    throw 'Expected the Danslicer.App.exe driver profile.'
}
function Set-Element([System.Xml.XmlNode] $parent, [string] $name, [string] $value) {
    $element = $parent.SelectSingleNode($name)
    if ($null -eq $element) { $element = $profile.CreateElement($name); [void] $parent.AppendChild($element) }
    $element.InnerText = $value
}
$properties = $profile.SelectSingleNode('/AppCfg/CfgProperties')
$signature = $profile.SelectSingleNode('/AppCfg/AppInfo/Signature')
$options = $profile.SelectSingleNode('/AppCfg/AppInfo/Options')
if ($null -eq $options) {
    $options = $profile.CreateElement('Options')
    [void] $profile.SelectSingleNode('/AppCfg/AppInfo').AppendChild($options)
}
Set-Element $properties 'InheritsFromID' 'ID_Default_Cfg'
Set-Element $signature 'Transport' 'RawInput'
$oldIdentity = $signature.SelectSingleNode('SiOpenAppName')
if ($null -ne $oldIdentity) { [void] $signature.RemoveChild($oldIdentity) }
Set-Element $options 'UseSiOpenAppName' 'false'
Set-Element $options 'IgnoreMouseWheelInertia' 'true'

# If this profile itself mapped an axis to mouse/keyboard emulation, restore native delivery.
# Keep each existing axis's sensitivity, reversal, enabled state and range settings.
foreach ($axis in $profile.SelectNodes('/AppCfg/Devices/Device/AxisBank/Axis')) {
    $inputAction = $axis.SelectSingleNode('Input/ActionID')
    $outputAction = $axis.SelectSingleNode('Output/ActionID')
    if ($null -ne $inputAction -and $null -ne $outputAction -and
        $inputAction.InnerText -match '^HIDMultiAxis_(X|Y|Z|Rx|Ry|Rz)$') {
        $outputAction.InnerText = $inputAction.InnerText
    }
}
$backup = $resolvedPath + '.' + (Get-Date -Format 'yyyyMMdd-HHmmssfff') + '.bak'
Copy-Item -LiteralPath $resolvedPath -Destination $backup
$profile.Save($resolvedPath)
Write-Output ('Updated: ' + $resolvedPath)
Write-Output ('Backup: ' + $backup)
