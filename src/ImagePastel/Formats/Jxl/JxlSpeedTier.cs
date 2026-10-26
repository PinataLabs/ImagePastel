// Copyright (c) PinataLabs.
// Licensed under the Apache License, Version 2.0.
// Ported from libjxl (https://github.com/libjxl/libjxl) at commit ef67fde2:
// lib/jxl/common.h (SpeedTier).
// Copyright (c) the JPEG XL Project Authors. BSD 3-Clause License; see THIRD-PARTY-NOTICES.TXT.

namespace PinataLabs.ImagePastel.Formats.Jxl;

/// <summary>
/// libjxl's encoder speed tiers, which its code branches on. libjxl's effort <c>e</c> is tier
/// <c>10 - e</c>; <see cref="JxlEffort.Fastest"/> is effort 3 (<see cref="Falcon"/>), <see cref="JxlEffort.Default"/>
/// effort 5 (<see cref="Hare"/>) and <see cref="JxlEffort.Thorough"/> effort 7 (<see cref="Squirrel"/>).
/// </summary>
internal enum JxlSpeedTier
{
    /// <summary>Try multiple combinations of Glacier flags for modular mode. Otherwise like Glacier.</summary>
    TectonicPlate = -1,

    /// <summary>Learn a global tree in Modular mode.</summary>
    Glacier = 0,

    /// <summary>Turns on FindBestQuantizationHQ loop.</summary>
    Tortoise = 1,

    /// <summary>Turns on FindBestQuantization butteraugli loop.</summary>
    Kitten = 2,

    /// <summary>Turns on dots, patches, and spline detection by default, as well as full context clustering.</summary>
    Squirrel = 3,

    /// <summary>Turns on error diffusion and full AC strategy heuristics.</summary>
    Wombat = 4,

    /// <summary>Turns on simple heuristics for AC strategy, quant field and gaborish by default.</summary>
    Hare = 5,

    /// <summary>Turns on clustering and enables coefficient reordering.</summary>
    Cheetah = 6,

    /// <summary>Turns off most encoder features. Does context clustering. Modular: uses a fixed tree with the Weighted predictor.</summary>
    Falcon = 7,

    /// <summary>Fastest setting for VarDCT. Modular: uses a fixed tree with the Gradient predictor.</summary>
    Thunder = 8,

    /// <summary>VarDCT: same as Thunder. Modular: no tree, Gradient predictor, fast histograms.</summary>
    Lightning = 9,
}
