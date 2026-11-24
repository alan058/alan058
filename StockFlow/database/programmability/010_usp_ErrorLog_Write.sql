/*
    Writes the error currently being handled to dbo.ErrorLog.
    Call from a CATCH block AFTER rolling back, so the log row is not rolled back too.
    Business-rule errors (50000+) are expected and are not logged.
*/
CREATE OR ALTER PROCEDURE dbo.usp_ErrorLog_Write
AS
BEGIN
    SET NOCOUNT ON;

    IF ERROR_NUMBER() IS NULL OR ERROR_NUMBER() >= 50000
        RETURN;

    INSERT dbo.ErrorLog (ErrorNumber, ErrorSeverity, ErrorState, ErrorProcedure, ErrorLine, ErrorMessage)
    VALUES (ERROR_NUMBER(), ERROR_SEVERITY(), ERROR_STATE(), ERROR_PROCEDURE(), ERROR_LINE(), ERROR_MESSAGE());
END
GO
