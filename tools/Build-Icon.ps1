param([string]$Source='src/DIPlayer/Assets/bulldog-logo.png',[string]$Output='src/DIPlayer/Assets/diplayer.ico')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationCore,WindowsBase
$bitmap=New-Object System.Windows.Media.Imaging.BitmapImage
$bitmap.BeginInit()
$bitmap.UriSource=[Uri](Resolve-Path -LiteralPath $Source).Path
$bitmap.CacheOption=[System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad
$bitmap.DecodePixelHeight=256
$bitmap.EndInit()
$bitmap.Freeze()
$sizes=@(16,20,24,32,40,48,64,128,256)
$frames=@()
foreach($size in $sizes) {
    $visual=New-Object System.Windows.Media.DrawingVisual
    [System.Windows.Media.RenderOptions]::SetBitmapScalingMode($visual,[System.Windows.Media.BitmapScalingMode]::HighQuality)
    $drawing=$visual.RenderOpen()
    $scale=[Math]::Min($size/$bitmap.PixelWidth,$size/$bitmap.PixelHeight)
    $width=$bitmap.PixelWidth*$scale
    $height=$bitmap.PixelHeight*$scale
    $rect=New-Object System.Windows.Rect (($size-$width)/2),(($size-$height)/2),$width,$height
    $drawing.DrawImage($bitmap,$rect)
    $drawing.Close()
    $render=New-Object System.Windows.Media.Imaging.RenderTargetBitmap $size,$size,96,96,([System.Windows.Media.PixelFormats]::Pbgra32)
    $render.Render($visual)
    $encoder=New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $encoder.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($render))
    $memory=New-Object System.IO.MemoryStream
    $encoder.Save($memory)
    $frames+=,@($memory.ToArray())
    $memory.Dispose()
}
$stream=[IO.File]::Create((Join-Path $PWD $Output))
$writer=New-Object IO.BinaryWriter $stream
try {
    $writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$sizes.Count)
    $offset=6+16*$sizes.Count
    for($i=0;$i -lt $sizes.Count;$i++) {
        $dimension=if($sizes[$i] -eq 256){0}else{$sizes[$i]}
        $writer.Write([byte]$dimension);$writer.Write([byte]$dimension)
        $writer.Write([byte]0);$writer.Write([byte]0)
        $writer.Write([uint16]1);$writer.Write([uint16]32)
        $writer.Write([uint32]$frames[$i].Length);$writer.Write([uint32]$offset)
        $offset+=$frames[$i].Length
    }
    foreach($frame in $frames){$writer.Write([byte[]]$frame)}
}finally{$writer.Dispose();$stream.Dispose()}
Write-Output ('Created multi-resolution icon: '+$Output)
