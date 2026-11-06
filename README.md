To solve urp incompatible issue on unity 6.3 LTS, try this on project root powershell

```
Remove-Item ".\Packages\com.unity.render-pipelines.universal" -Recurse -Force -ErrorAction SilentlyContinue

$src = Get-ChildItem ".\Library\PackageCache" -Directory -Filter "com.unity.render-pipelines.universal@*" |
    Select-Object -First 1

Copy-Item $src.FullName ".\Packages\com.unity.render-pipelines.universal" -Recurse

$dst = ".\Packages\com.unity.render-pipelines.universal\Editor\Tools\Converters\ReadonlyMaterialConverter"
$base = "https://raw.githubusercontent.com/Unity-Technologies/Graphics/6000.3/staging/Packages/com.unity.render-pipelines.universal/Editor/Tools/Converters/ReadonlyMaterialConverter"

$files = @(
    "ReadonlyMaterialConverter.MaterialReferenceBuilder.cs",
    "ReadonlyMaterialConverter.MaterialReferenceBuilder.cs.meta",
    "ReadonlyMaterialConverter.MaterialReferenceChanger.cs",
    "ReadonlyMaterialConverter.MaterialReferenceChanger.cs.meta"
)

foreach ($file in $files) {
    Invoke-WebRequest -Uri "$base/$file" -OutFile "$dst\$file"
}

Remove-Item ".\Library\ScriptAssemblies" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item ".\Library\BurstCache" -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item ".\Library\Bee" -Recurse -Force -ErrorAction SilentlyContinue
```
