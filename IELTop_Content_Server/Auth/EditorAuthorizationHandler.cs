using IELTop_Content_Server.Services;
using Microsoft.AspNetCore.Authorization;

namespace IELTop_Content_Server.Auth;

/// <summary>
/// Editor access for the review queue. An admin always qualifies. A
/// contributor qualifies only while an approved editor application
/// matches their signed in email, checked against the store on every
/// request so revoking the role takes effect at once.
/// </summary>
public sealed class EditorRequirement : IAuthorizationRequirement;

public sealed class EditorAuthorizationHandler(
    IEditorService editors,
    ILogger<EditorAuthorizationHandler> logger)
    : AuthorizationHandler<EditorRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, EditorRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return;

        // An admin is also an editor.
        if (context.User.IsInRole("Admin"))
        {
            context.Succeed(requirement);
            return;
        }

        if (!context.User.IsInRole("Contributor"))
            return;

        string? email = context.User.Identity.Name;
        if (string.IsNullOrWhiteSpace(email))
            return;

        try
        {
            var approved = await editors.ListAsync(
                Models.EditorApplicationStatus.Approved, CancellationToken.None);
            if (editors.IsEditor(email, approved))
                context.Succeed(requirement);
        }
        catch (Exception e)
        {
            // A store blip must not silently grant the role.
            logger.LogWarning(e, "Could not check editor status for {Email}", email);
        }
    }
}
