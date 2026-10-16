using ClinicManagement.Application.Common.Exceptions;
using ClinicManagement.Application.Diagnostics.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ClinicManagement.Api.Controllers;

[ApiController]
[Route("api/v1/diagnostic-attachments")]
[Authorize]
public class DiagnosticAttachmentsController(IDiagnosticWorkflowService workflow, IDiagnosticAttachmentStorage storage) : ControllerBase
{
    [HttpGet("{id}/content")]
    public async Task<IActionResult> GetContent(long id)
    {
        var attachment = await workflow.GetAttachmentForCurrentUserAsync(id);
        var stream = storage.OpenRead(attachment.StoredFileName)
            ?? throw new NotFoundException("Tệp đính kèm không tồn tại.");
        Response.Headers.ContentDisposition = $"inline; filename*=UTF-8''{Uri.EscapeDataString(attachment.OriginalFileName)}";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.CacheControl = "no-store";
        return File(stream, attachment.ContentType);
    }
}
