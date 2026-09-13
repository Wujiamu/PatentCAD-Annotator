param(
    [Parameter(Mandatory = $true)]
    [string]$Path
)

$ErrorActionPreference = "Stop"
$resolved = (Resolve-Path -LiteralPath $Path).Path
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Read-EntryText([IO.Compression.ZipArchive]$Archive, [string]$Name) {
    $entry = $Archive.GetEntry($Name)
    if ($null -eq $entry) { throw "DOTM package lacks required entry: $Name" }
    $reader = [IO.StreamReader]::new($entry.Open(), [Text.Encoding]::UTF8, $true)
    try { $reader.ReadToEnd() } finally { $reader.Dispose() }
}

$archive = [IO.Compression.ZipFile]::OpenRead($resolved)
try {
    $entryNames = @($archive.Entries | ForEach-Object { $_.FullName })
    foreach ($requiredEntry in @(
        "[Content_Types].xml", "word/vbaProject.bin", "word/vbaData.xml",
        "word/_rels/vbaProject.bin.rels"
    )) {
        if ($entryNames -notcontains $requiredEntry) {
            throw "DOTM package lacks required entry: $requiredEntry"
        }
    }

    [xml]$contentTypes = Read-EntryText $archive "[Content_Types].xml"
    $contentTypeManager = [Xml.XmlNamespaceManager]::new($contentTypes.NameTable)
    $contentTypeManager.AddNamespace("ct", "http://schemas.openxmlformats.org/package/2006/content-types")
    $vbaOverride = $contentTypes.SelectSingleNode(
        "/ct:Types/ct:Override[@PartName='/word/vbaData.xml']",
        $contentTypeManager
    )
    if ($null -eq $vbaOverride -or $vbaOverride.GetAttribute("ContentType") -ne "application/vnd.ms-word.vbaData+xml") {
        throw "DOTM package lacks the VBA supplemental-data content type"
    }

    [xml]$relationships = Read-EntryText $archive "word/_rels/vbaProject.bin.rels"
    $relationshipManager = [Xml.XmlNamespaceManager]::new($relationships.NameTable)
    $relationshipManager.AddNamespace("r", "http://schemas.openxmlformats.org/package/2006/relationships")
    $relationshipType = "http://schemas.microsoft.com/office/2006/relationships/wordVbaData"
    $vbaRelationships = @($relationships.SelectNodes(
        "/r:Relationships/r:Relationship[@Type='$relationshipType']",
        $relationshipManager
    ))
    if ($vbaRelationships.Count -ne 1 -or $vbaRelationships[0].GetAttribute("Target") -ne "vbaData.xml") {
        throw "DOTM package lacks the unique VBA supplemental-data relationship"
    }

    [xml]$vbaData = Read-EntryText $archive "word/vbaData.xml"
    $namespace = [Xml.XmlNamespaceManager]::new($vbaData.NameTable)
    $namespace.AddNamespace("wne", "http://schemas.microsoft.com/office/word/2006/wordml")
    $macroNodes = @($vbaData.SelectNodes("/wne:vbaSuppData/wne:mcds/wne:mcd", $namespace))
    $macroNames = @($macroNodes | ForEach-Object { $_.GetAttribute("macroName", "http://schemas.microsoft.com/office/word/2006/wordml") })
    $expected = @(
        "PATENTMARKERADDIN.AUTOEXPORT.SHOWPATENTDICTPANEL",
        "PATENTMARKERADDIN.PATENTMARKERBOOTSTRAP.AUTOEXEC",
        "PATENTMARKERADDIN.PATENTMARKERBOOTSTRAP.AUTOEXIT"
    )
    foreach ($macroName in $expected) {
        if ($macroNames -notcontains $macroName) {
            throw "DOTM macro discovery metadata lacks: $macroName"
        }
    }
    $unexpected = @($macroNames | Where-Object { $_ -notin $expected })
    if ($unexpected.Count -gt 0) {
        throw "DOTM macro discovery metadata exposes unexpected macros: $($unexpected -join ', ')"
    }
    if ($macroNames.Count -ne $expected.Count) {
        throw "DOTM macro discovery metadata count is $($macroNames.Count), expected $($expected.Count)"
    }

    Write-Output "PASS|DOTM_PACKAGE|path=$resolved|vba_project=true|content_type=true|relationship=true|macro_metadata=true|public_macros=$($macroNames.Count)"
} finally {
    $archive.Dispose()
}
