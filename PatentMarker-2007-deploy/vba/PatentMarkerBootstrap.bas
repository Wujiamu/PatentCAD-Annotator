Attribute VB_Name = "PatentMarkerBootstrap"
Option Explicit
' Private lifecycle procedures stay out of Alt+F8; DOTM metadata retains discovery.
' Word runs AutoExec when this project is loaded as a global template.
Private Sub AutoExec()
    Call AutoExport.InitializeAutoExport("AutoExec")
End Sub

' Release the application event sink when Word unloads the global template.
Private Sub AutoExit()
    Call AutoExport.ReleaseAutoExportForShutdown
End Sub
