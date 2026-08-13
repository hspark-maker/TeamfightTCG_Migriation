using System;
using System.Collections.Generic;

[Serializable]
public class KeywordGrowthSaveData
{
    public const int VERSION = 1;

    public int version = VERSION;
    public List<KeywordGrowthEntry> entries = new List<KeywordGrowthEntry>();
}

[Serializable]
public class KeywordGrowthEntry
{
    public int keyword;
    public int level;
}
