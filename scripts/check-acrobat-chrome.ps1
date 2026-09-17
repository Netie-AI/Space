# Self-check: docked WPF document window (ledger row 49; supercedes row 48 overlay)

$ErrorActionPreference = "Stop"
$root = "D:\Space\src\NetieSpace"
$fail = @()

function Assert-Contains($path, $needle, $label) {
  $t = Get-Content -Raw $path
  if ($t -notlike "*$needle*") { $script:fail += $label }
}

$v = "$root\Assets\pdfjs\netie-viewer.html"
$x = "$root\Views\PreviewWindow.xaml"
$c = "$root\Views\PreviewWindow.xaml.cs"

Assert-Contains $v "html.canvas-only #toolbar" "canvas-only hides toolbar"
Assert-Contains $v "html.canvas-only #pageHud" "canvas-only hides HUD"
Assert-Contains $v "canvasOnly" "canvasOnly host flag"
Assert-Contains $v 'id="prev"' "prev id kept"
Assert-Contains $v 'id="pageHud"' "pageHud node kept"
Assert-Contains $v 'id="fillBtn"' "fillBtn kept"
Assert-Contains $v 'class="doc-app"' "html.doc-app default"
Assert-Contains $v "classList.add('doc-app')" "host keeps light doc-app"
Assert-Contains $v "align-items: center" "viewer centers page"
Assert-Contains $x 'x:Name="DocNavBar"' "DocNavBar"
Assert-Contains $x 'x:Name="DocAppHeader"' "DocAppHeader"
Assert-Contains $x 'x:Name="DocLeftNav"' "DocLeftNav"
Assert-Contains $x 'x:Name="DocFileTab"' "DocFileTab"
Assert-Contains $x 'x:Name="PdfSaveStampsButton"' "Save stays named"
Assert-Contains $x 'x:Name="PdfSpaceRail"' "PdfSpaceRail"
Assert-Contains $x 'DocModeOn' "mode icon styles"
Assert-Contains $c "ApplyWorkbenchShell" "ApplyWorkbenchShell"
Assert-Contains $c "canvasOnly" "host canvasOnly"
Assert-Contains $c "PostDocChrome" "PostDocChrome thumbs"
Assert-Contains $c "DocPrev_Click" "native Prev"
Assert-Contains $c "DocNavBar.Visibility = on ? Visibility.Visible" "docked nav when workbench"
Assert-Contains $c "DocAppHeader.Visibility = on ? Visibility.Visible" "docked header when workbench"
Assert-Contains $c "PdfSpaceRail.Visibility = on ? Visibility.Visible" "tools rail when workbench"
Assert-Contains $c "SetRibbonFloated(false)" "ribbon stays docked"
Assert-Contains $x 'x:Name="DocThumbsList"' "WPF thumbs list"
Assert-Contains $x 'x:Name="ContentWellInner"' "inner well radius"
Assert-Contains $c "new GridLength(((on || lightEdit) && _pdfThumbsOn) ? 120 : 0)" "120px thumbs pane"
Assert-Contains $c "var thumbs = !(workbench || edit);" "HTML thumbs off in workbench"
Assert-Contains $c 'type == "thumb"' "host accepts thumb"
Assert-Contains $c "MinWidth = 1100" "workbench min width"
Assert-Contains $v "dumpThumbsToHost" "viewer posts thumbs when hidden"
Assert-Contains $v "loadThumbs" "load more thumbs from host"
$a = "$root\App.xaml.cs"
Assert-Contains $v "html.slim-chrome #toolbar" "slim-chrome hides toolbar"
Assert-Contains $v "html.slim-chrome #banner" "slim-chrome hides banner"
Assert-Contains $x 'x:Name="DocFloatDoneBtn"' "Done node kept"
Assert-Contains $x 'x:Name="DocToolFloat"' "overlay node kept (not used)"
Assert-Contains $x 'x:Name="DocZoomFloat"' "zoom HUD node kept"
Assert-Contains $c "SetRibbonFloated" "reparent helper kept"
Assert-Contains $a "Space swallowed -- PDF workbench is an app" "Space does not leave workbench"

Assert-Contains $c "SetPdfWorkbenchAsync" "enter/leave workbench API"
Assert-Contains $a "NoteSpaceTap" "double-tap Space"
Assert-Contains $a "GetDoubleClickTime" "Windows double-click window"
Assert-Contains $a "ArgsWantEdit" "CLI --edit"
Assert-Contains $a "enterPdfWorkbench" "OpenPreviewAsync edit flag"

$vt = Get-Content -Raw $v
if ($vt -match 'id="save"' -or $vt -match 'Clear page') {
  $fail += "viewer must not add Save/Clear page"
}

$ct = Get-Content -Raw $c
if ($ct -match 'SetRibbonFloated\(on\)') {
  $fail += "workbench must not float the ribbon"
}

if ($fail.Count -gt 0) {
  Write-Host "FAIL: $($fail -join ', ')"
  exit 1
}
Write-Host "OK: docked WPF chrome; 120px thumbs; WebView canvas-only; glue light doc-app"
exit 0
