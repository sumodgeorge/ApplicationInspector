﻿using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace Microsoft.ApplicationInspector.RulesEngine
{
    using System;

    [Flags]
    [JsonConverter(typeof(StringEnumConverter))]
    public enum Confidence { Unspecified = 0, Low = 1, Medium = 2, High = 4 }
}