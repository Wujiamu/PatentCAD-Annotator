param(
    [Parameter(Mandatory = $true)]
    [string]$Path
)

$ErrorActionPreference = "Stop"
$resolved = (Resolve-Path -LiteralPath $Path).Path
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$utf8 = [Text.UTF8Encoding]::new($false)

function Read-EntryText([IO.Compression.ZipArchive]$Archive, [string]$Name) {
    $entry = $Archive.GetEntry($Name)
    if ($null -eq $entry) { return $null }
    $reader = [IO.StreamReader]::new($entry.Open(), [Text.Encoding]::UTF8, $true)
    try { $reader.ReadToEnd() } finally { $reader.Dispose() }
}

function Write-EntryText([IO.Compression.ZipArchive]$Archive, [string]$Name, [string]$Content) {
    $existing = $Archive.GetEntry($Name)
    if ($null -ne $existing) { $existing.Delete() }
    $entry = $Archive.CreateEntry($Name, [IO.Compression.CompressionLevel]::Optimal)
    $writer = [IO.StreamWriter]::new($entry.Open(), $utf8)
    try { $writer.Write($Content) } finally { $writer.Dispose() }
}

function Xml-ToString([xml]$Document) {
    $settings = [Xml.XmlWriterSettings]::new()
    $settings.Encoding = $utf8
    $settings.Indent = $false
    $settings.OmitXmlDeclaration = $false
    $memory = [IO.MemoryStream]::new()
    $writer = [Xml.XmlWriter]::Create($memory, $settings)
    try {
        $Document.Save($writer)
        $writer.Flush()
        $utf8.GetString($memory.ToArray())
    } finally {
        $writer.Dispose()
        $memory.Dispose()
    }
}

$stream = [IO.File]::Open($resolved, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
$archive = [IO.Compression.ZipArchive]::new($stream, [IO.Compression.ZipArchiveMode]::Update, $false)
try {
    if ($null -eq $archive.GetEntry("word/vbaProject.bin")) {
        throw "DOTM has no word/vbaProject.bin"
    }

    $contentTypesText = Read-EntryText $archive "[Content_Types].xml"
    if ($null -eq $contentTypesText) { throw "DOTM has no [Content_Types].xml" }
    [xml]$contentTypes = $contentTypesText
    $contentTypesNs = "http://schemas.openxmlformats.org/package/2006/content-types"
    $contentTypeManager = [Xml.XmlNamespaceManager]::new($contentTypes.NameTable)
    $contentTypeManager.AddNamespace("ct", $contentTypesNs)
    $vbaOverride = $contentTypes.SelectSingleNode("/ct:Types/ct:Override[@PartName='/word/vbaData.xml']", $contentTypeManager)
    if ($null -eq $vbaOverride) {
        $vbaOverride = $contentTypes.CreateElement("Override", $contentTypesNs)
        $vbaOverride.SetAttribute("PartName", "/word/vbaData.xml")
        $vbaOverride.SetAttribute("ContentType", "application/vnd.ms-word.vbaData+xml")
        [void]$contentTypes.DocumentElement.AppendChild($vbaOverride)
    } elseif ($vbaOverride.GetAttribute("ContentType") -ne "application/vnd.ms-word.vbaData+xml") {
        throw "Existing /word/vbaData.xml content type is unexpected"
    }
    Write-EntryText $archive "[Content_Types].xml" (Xml-ToString $contentTypes)

    $relationshipName = "word/_rels/vbaProject.bin.rels"
    $relationshipText = Read-EntryText $archive $relationshipName
    $relationshipsNs = "http://schemas.openxmlformats.org/package/2006/relationships"
    if ($null -eq $relationshipText) {
        $relationships = [Xml.XmlDocument]::new()
        [void]$relationships.AppendChild($relationships.CreateXmlDeclaration("1.0", "UTF-8", "yes"))
        $root = $relationships.CreateElement("Relationships", $relationshipsNs)
        [void]$relationships.AppendChild($root)
    } else {
        [xml]$relationships = $relationshipText
    }
    $relationshipManager = [Xml.XmlNamespaceManager]::new($relationships.NameTable)
    $relationshipManager.AddNamespace("r", $relationshipsNs)
    $relationshipType = "http://schemas.microsoft.com/office/2006/relationships/wordVbaData"
    $vbaRelationship = $relationships.SelectSingleNode("/r:Relationships/r:Relationship[@Type='$relationshipType']", $relationshipManager)
    if ($null -eq $vbaRelationship) {
        $usedIds = @($relationships.SelectNodes("/r:Relationships/r:Relationship", $relationshipManager) | ForEach-Object { $_.GetAttribute("Id") })
        $index = 1
        while ($usedIds -contains "rId$index") { $index++ }
        $vbaRelationship = $relationships.CreateElement("Relationship", $relationshipsNs)
        $vbaRelationship.SetAttribute("Id", "rId$index")
        $vbaRelationship.SetAttribute("Type", $relationshipType)
        $vbaRelationship.SetAttribute("Target", "vbaData.xml")
        [void]$relationships.DocumentElement.AppendChild($vbaRelationship)
    } elseif ($vbaRelationship.GetAttribute("Target") -ne "vbaData.xml") {
        throw "Existing VBA supplemental-data relationship target is unexpected"
    }
    Write-EntryText $archive $relationshipName (Xml-ToString $relationships)

    $macros = @(
        @{ Macro = "PATENTMARKERADDIN.AUTOEXPORT.SHOWPATENTDICTPANEL"; Name = "PatentMarkerAddin.AutoExport.ShowPatentDictPanel" },
        @{ Macro = "PATENTMARKERADDIN.PATENTMARKERBOOTSTRAP.AUTOEXEC"; Name = "PatentMarkerAddin.PatentMarkerBootstrap.AutoExec" },
        @{ Macro = "PATENTMARKERADDIN.PATENTMARKERBOOTSTRAP.AUTOEXIT"; Name = "PatentMarkerAddin.PatentMarkerBootstrap.AutoExit" }
    )
    $macroXml = foreach ($macro in $macros) {
        '<wne:mcd wne:macroName="' + $macro.Macro + '" wne:name="' + $macro.Name + '" wne:bEncrypt="00" wne:cmg="56"/>'
    }
    $vbaData = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>' + "`r`n" +
        '<wne:vbaSuppData xmlns:wne="http://schemas.microsoft.com/office/word/2006/wordml"><wne:mcds>' +
        ($macroXml -join '') + '</wne:mcds></wne:vbaSuppData>'
    Write-EntryText $archive "word/vbaData.xml" $vbaData
} finally {
    $archive.Dispose()
    $stream.Dispose()
}

Write-Output "PASS|DOTM_FINALIZED|path=$resolved|vba_supplemental_data=true"
