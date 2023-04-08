using System.Drawing;
using Wavee.Audio.Meta.ColorMode;
using Wavee.Audio.Meta.Metadata;

namespace Wavee.Audio.Meta;

/// <summary>
/// A <see cref="Visual"/> is any 2 dimensional graphic.
/// </summary>
/// <param name="MediaType">The Media Type (MIME Type) used to encode the <see cref="Visual"/>.</param>
/// <param name="Dimensions">
/// The dimensions of the <see cref="Visual"/>.
///
/// Note: This value may not be accurate as it comes from metadata, not the embedded graphic
/// itself. Consider it only a hint.
/// </param>
/// <param name="BitsPerPixel">
/// The number of bits-per-pixel (aka bit-depth) of the unencoded image.
///
/// Note: This value may not be accurate as it comes from metadata, not the embedded graphic
/// itself. Consider it only a hint.
/// </param>
/// <param name="ColorMode">
/// The color mode of the <see cref="Visual"/>.
///
/// Note: This value may not be accurate as it comes from metadata, not the embedded graphic
/// itself. Consider it only a hint.
/// </param>
/// <param name="Usage">The usage and/or content of the <see cref="Visual"/>.</param>
/// <param name="Tags">Any tags associated with the <see cref="Visual"/>.</param>
/// <param name="Data">The data of the <see cref="Visual"/>, encoded as per <see cref="MediaType"/>.</param>
public record Visual(
    string MediaType,
    Size? Dimensions,
    uint BitsPerPixel,
    IColorMode? ColorMode,
    StandardVisualKey? Usage,
    Tag[] Tags,
    byte[] Data
);