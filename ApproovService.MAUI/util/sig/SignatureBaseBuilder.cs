// ApproovService.MAUI/util/sig/SignatureBaseBuilder.cs
using System.Text;
using Approov.Util.HttpSfv;

namespace Approov.Util.Sig;

public static class SignatureBaseBuilder
{
    public static string Build(SignatureParameters signatureParams, IComponentProvider provider)
    {
        var sb = new StringBuilder();
        var componentIdentifiers = signatureParams.ToComponentValue();

        // Append component identifier lines: "<id>": <value>\n
        foreach (var item in componentIdentifiers)
        {
            sb.Append(SFV.SerializeStringItem(item));
            sb.Append(": ");
            sb.Append(provider.GetComponentValue(item.Value));
            sb.Append('\n');
        }

        // Build the @signature-params inner list
        var innerListItems = componentIdentifiers.ToList();
        var sfvInnerList = SFV.SerializeInnerList(innerListItems);

        // Append parameters after the inner list: ;key=value
        var paramsStr = new StringBuilder();
        foreach (var (key, val) in signatureParams.GetParameters())
        {
            paramsStr.Append(';');
            paramsStr.Append(key);
            paramsStr.Append('=');
            paramsStr.Append(SFV.SerializeBareItem(val));
        }

        sb.Append("\"@signature-params\": ");
        sb.Append(sfvInnerList);
        sb.Append(paramsStr);

        return sb.ToString();
    }

    public static string BuildSignatureParamsValue(SignatureParameters signatureParams)
    {
        var componentIdentifiers = signatureParams.ToComponentValue();
        var sfvInnerList = SFV.SerializeInnerList(componentIdentifiers.ToList());
        var paramsStr = new StringBuilder();
        foreach (var (key, val) in signatureParams.GetParameters())
        {
            paramsStr.Append(';');
            paramsStr.Append(key);
            paramsStr.Append('=');
            paramsStr.Append(SFV.SerializeBareItem(val));
        }
        return sfvInnerList.ToString() + paramsStr.ToString();
    }
}
