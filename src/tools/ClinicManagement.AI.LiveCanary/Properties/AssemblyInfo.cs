using Microsoft.AspNetCore.Mvc.Testing;
using System.Runtime.CompilerServices;

// The canary is an executable rather than an xUnit test assembly, so the
// MVC-testing SDK cannot generate this metadata for WebApplicationFactory.
// Keep the content root portable and anchor it to the repository solution.
[assembly: WebApplicationFactoryContentRoot(
    "ClinicManagement.Api, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
    "../../../../../../src/backend/ClinicManagement.Api",
    "appsettings.json",
    "0")]
[assembly: InternalsVisibleTo("ClinicManagement.IntegrationTests")]
