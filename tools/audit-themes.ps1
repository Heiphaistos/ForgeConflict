$ErrorActionPreference = 'Stop'

function Lum([string]$hex) {
    $r = [Convert]::ToInt32($hex.Substring(1,2),16) / 255
    $g = [Convert]::ToInt32($hex.Substring(3,2),16) / 255
    $b = [Convert]::ToInt32($hex.Substring(5,2),16) / 255
    function Ch($s) { if ($s -le 0.03928) { $s / 12.92 } else { [Math]::Pow(($s + 0.055) / 1.055, 2.4) } }
    return (0.2126 * (Ch $r)) + (0.7152 * (Ch $g)) + (0.0722 * (Ch $b))
}
function Contraste([string]$a, [string]$b) {
    $la = Lum $a; $lb = Lum $b
    if ($la -gt $lb) { return ($la + 0.05) / ($lb + 0.05) }
    return ($lb + 0.05) / ($la + 0.05)
}

# Extrait les themes du catalogue tel qu'il est ecrit, pour auditer la source de verite.
$src = Get-Content 'D:\Projet\ForgeConflict\src\ForgeConflict.App\Theme\ThemeCatalog.cs' -Raw
$blocs = [regex]::Matches($src, 'Id:\s*"(?<id>[^"]+)"[\s\S]*?SurfaceRaised:\s*C\("(?<sr>#[0-9A-Fa-f]{6})"\)[\s\S]*?TextPrimary:\s*C\("(?<tp>#[0-9A-Fa-f]{6})"\),\s*TextSecondary:\s*C\("(?<ts>#[0-9A-Fa-f]{6})"\)[\s\S]*?Healthy:\s*C\("(?<h>#[0-9A-Fa-f]{6})"\),\s*Warning:\s*C\("(?<w>#[0-9A-Fa-f]{6})"\),\s*Critical:\s*C\("(?<c>#[0-9A-Fa-f]{6})"\)')

"{0,-12} {1,8} {2,8} {3,8} {4,9}" -f 'theme', 'C:sain', 'C:attn', 'C:crit', 'dLum(s/a)'
"-" * 54
foreach ($m in $blocs) {
    $id = $m.Groups['id'].Value
    $sr = $m.Groups['sr'].Value
    $h = $m.Groups['h'].Value; $w = $m.Groups['w'].Value; $c = $m.Groups['c'].Value

    $ch = Contraste $h $sr; $cw = Contraste $w $sr; $cc = Contraste $c $sr
    $d = [Math]::Abs((Lum $h) - (Lum $w))

    $flag = ''
    if ($ch -lt 3 -or $cw -lt 3 -or $cc -lt 3) { $flag += ' CONTRASTE' }
    if ($d -lt 0.05) { $flag += ' LUMINANCE' }

    "{0,-12} {1,8:0.00} {2,8:0.00} {3,8:0.00} {4,9:0.000}{5}" -f $id, $ch, $cw, $cc, $d, $flag
}
