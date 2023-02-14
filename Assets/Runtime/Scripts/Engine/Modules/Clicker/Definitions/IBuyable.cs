using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace io.github.thisisnozaku.idle.framework.Engine.Modules.Clicker.Definitions
{
    public interface IBuyable
    {
        Dictionary<string, string> CostExpressions { get; }
    }
}