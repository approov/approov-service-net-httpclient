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
            string value = provider.GetComponentValue(item.Value);
            sb.Append('"');
            sb.Append(item.Value);
            sb.Append("\": ");
            sb.Append(value);
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
            // Integers are serialized without quotes; strings with quotes
            if (val is long || val is int)
                paramsStr.Append(val);
            else
            {
                paramsStr.Append('"');
                paramsStr.Append(val);
                paramsStr.Append('"');
            }
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
            if (val is long || val is int)
                paramsStr.Append(val);
            else
            {
                paramsStr.Append('"');
                paramsStr.Append(val);
                paramsStr.Append('"');
            }
        }
        return sfvInnerList.ToString() + paramsStr.ToString();
    }
}
