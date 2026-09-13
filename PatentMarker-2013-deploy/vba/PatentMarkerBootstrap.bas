Attribute VB_Name = "PatentMarkerBootstrap"
Option Explicit
' Word runs AutoExec when this project is loaded as a global template.
Public Sub AutoExec()
    Call AutoExport.InitializeAutoExport("AutoExec")
End Sub

' Release the application event sink when Word unloads the global template.
Public Sub AutoExit()
    Call AutoExport.ReleaseAutoExportForShutdown
End Sub
