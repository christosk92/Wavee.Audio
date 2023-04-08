namespace Wavee.Audio.Meta;

/// <summary>
/// <see cref="VendorData"/> is any binary metadata that is proprietary to a certain application or vendor.
/// </summary>
/// <param name="Ident">A text representation of the vendor's application identifier.</param>
/// <param name="Data">The vendor data.</param>
public record VendorData(string Ident, byte[] Data);