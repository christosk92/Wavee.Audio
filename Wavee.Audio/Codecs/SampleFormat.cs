namespace Wavee.Audio.Codecs;

/// <summary>
/// SampleFormat describes the data encoding for an audio sample.
/// </summary>
public enum SampleFormat
{
    /// <summary>
    /// Unsigned 8-bit integer.
    /// </summary>
    U8,

    /// <summary>
    /// Unsigned 16-bit integer.
    /// </summary>
    U16,

    /// <summary>
    /// Unsigned 24-bit integer.
    /// </summary>
    U24,

    /// <summary>
    /// Unsigned 32-bit integer.
    /// </summary>
    U32,

    /// <summary>
    /// Signed 8-bit integer.
    /// </summary>
    S8,

    /// <summary>
    /// Signed 16-bit integer.
    /// </summary>
    S16,

    /// <summary>
    /// Signed 24-bit integer.
    /// </summary>
    S24,

    /// <summary>
    /// Signed 32-bit integer.
    /// </summary>
    S32,

    /// <summary>
    /// Single prevision (32-bit) floating point. Aka float
    /// </summary>
    F32,

    /// <summary>
    /// Double precision (64-bit) floating point. Aka double
    /// </summary>
    F64,
}