using Microsoft.AspNetCore.Mvc.ApplicationModels;

namespace Indus360.Api.BulkImportSupport;

/// <summary>
/// Prefixes every folded-in BulkImport controller's route with "bulk/" so its endpoints live
/// under <c>/bulk/api/...</c> instead of <c>/api/...</c>. This keeps them from colliding with
/// Indus360's own controllers — both apps ship Auth/Health/Keyline/MessageFormat controllers
/// on the same <c>api/[controller]</c> template. Applied ONLY to controllers whose type sits in
/// the <c>Backend.*</c> namespace (the copied BulkImport code); Indus360's own
/// <c>Indus360.Api.Controllers.*</c> controllers are left untouched.
/// </summary>
public sealed class BulkImportRoutePrefixConvention : IControllerModelConvention
{
    public void Apply(ControllerModel controller)
    {
        var ns = controller.ControllerType.Namespace ?? string.Empty;
        if (!ns.StartsWith("Backend.", StringComparison.Ordinal))
            return; // not a BulkImport controller — leave Indus360 routes as-is

        foreach (var selector in controller.Selectors)
        {
            if (selector.AttributeRouteModel is null)
                continue;

            selector.AttributeRouteModel = new AttributeRouteModel
            {
                Template = AttributeRouteModel.CombineTemplates(
                    "bulk", selector.AttributeRouteModel.Template),
            };
        }
    }
}
