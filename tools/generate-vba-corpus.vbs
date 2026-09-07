Option Explicit

Dim fso, scriptDir, workspace, vbaDir, batchDir, tempDir, logPath
Set fso = CreateObject("Scripting.FileSystemObject")
scriptDir = fso.GetParentFolderName(WScript.ScriptFullName)
workspace = fso.GetParentFolderName(scriptDir)

If WScript.Arguments.Count < 2 Then
    WScript.Echo "Usage: generate-vba-corpus.vbs <vba-dir> <corpus-dir>"
    WScript.Quit 2
End If
vbaDir = WScript.Arguments(0)
batchDir = WScript.Arguments(1)
vbaDir = fso.GetAbsolutePathName(vbaDir)
batchDir = fso.GetAbsolutePathName(batchDir)
tempDir = fso.BuildPath(fso.GetSpecialFolder(2), "PatentMarkerVbaCorpus-" & Replace(fso.GetTempName, ".tmp", ""))
fso.CreateFolder tempDir
logPath = fso.BuildPath(tempDir, "test.log")

Dim word, doc, macroPath, outputPath, moduleName
macroPath = fso.BuildPath(tempDir, "vba-corpus.docm")
outputPath = fso.BuildPath(batchDir, "vba-expected-v4-output.txt")

LogLine "START"
If Not fso.FolderExists(vbaDir) Then Fail "VBA_DIR_NOT_FOUND " & vbaDir
If Not fso.FolderExists(batchDir) Then Fail "CORPUS_DIR_NOT_FOUND " & batchDir

Set word = CreateObject("Word.Application")
word.Visible = False
word.DisplayAlerts = 0
Set doc = word.Documents.Add
doc.SaveAs2 macroPath, 13
LogLine "MACRO_DOCUMENT_SAVED"

For Each moduleName In Array("Patterns.bas", "DictModel.bas", "JsonWriter.bas", "PatentExtractor.bas", "clsSaveHook.cls", "AutoExport.bas")
    doc.VBProject.VBComponents.Import fso.BuildPath(vbaDir, moduleName)
Next
doc.Save
doc.Activate
LogLine "MODULES_IMPORTED"

Dim outText, index, file, text, root, entries, i, entry
outText = ""
index = 0
For Each file In fso.GetFolder(batchDir).Files
    If LCase(fso.GetExtensionName(file.Name)) = "txt" _
        And InStr(1, file.Name, "test-output", vbTextCompare) = 0 _
        And InStr(1, file.Name, "expected", vbTextCompare) = 0 Then
        index = index + 1
        text = ReadUtf8(file.Path)
        Set root = word.Run("DictModel.BuildModel", text, file.Name, "2026-08-05T00:00:00")
        entries = root("entries")
        outText = outText & "[" & index & "] " & file.Name & vbCrLf
        On Error Resume Next
        For i = LBound(entries) To UBound(entries)
            Set entry = entries(i)
            outText = outText & "    " & CStr(entry("number")) & "=" & CStr(entry("name")) & vbCrLf
        Next
        On Error GoTo 0
    End If
Next

WriteUtf8NoBom outputPath, outText
LogLine "CORPUS_WRITTEN files=" & index & " path=" & outputPath
LogLine "PASS"

doc.Close False
Set doc = Nothing
word.Quit
Set word = Nothing
WScript.Echo "PASS|" & outputPath & "|" & logPath
WScript.Quit 0

Function ReadUtf8(ByVal path)
    Dim stream
    Set stream = CreateObject("ADODB.Stream")
    stream.Type = 2
    stream.Charset = "utf-8"
    stream.Open
    stream.LoadFromFile path
    ReadUtf8 = stream.ReadText(-1)
    stream.Close
End Function

Sub WriteUtf8NoBom(ByVal path, ByVal content)
    Dim stream, bytes, outStream
    ' ADODB.Stream has no position 3 when an empty string produced no UTF-8
    ' preamble.  Write an empty binary file directly so an empty corpus still
    ' completes without leaving the Word automation server behind.
    If Len(content) = 0 Then
        Set outStream = CreateObject("ADODB.Stream")
        outStream.Type = 1
        outStream.Open
        outStream.SaveToFile path, 2
        outStream.Close
        Exit Sub
    End If

    Set stream = CreateObject("ADODB.Stream")
    stream.Type = 2
    stream.Charset = "utf-8"
    stream.Open
    stream.WriteText content
    stream.Position = 0
    stream.Type = 1
    stream.Position = 3
    bytes = stream.Read
    stream.Close

    Set outStream = CreateObject("ADODB.Stream")
    outStream.Type = 1
    outStream.Open
    outStream.Write bytes
    outStream.SaveToFile path, 2
    outStream.Close
End Sub

Sub LogLine(ByVal message)
    Dim logFile
    Set logFile = fso.OpenTextFile(logPath, 8, True, 0)
    logFile.WriteLine Now & " " & message
    logFile.Close
    WScript.Echo message
End Sub

Sub Fail(ByVal message)
    On Error Resume Next
    LogLine "FAIL " & message
    If Not doc Is Nothing Then doc.Close False
    If Not word Is Nothing Then word.Quit
    WScript.Echo "FAIL|" & message & "|" & logPath
    WScript.Quit 1
End Sub
