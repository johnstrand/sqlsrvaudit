using System;
public class Formatting {
    public static string FormatType(string name, short maxLength, byte precision, byte scale) {
        if (name is "nchar" or "nvarchar")
            return $"{name}({(maxLength == -1 ? "max" : (maxLength / 2).ToString())})";
        if (name is "char" or "varchar" or "binary" or "varbinary")
            return $"{name}({(maxLength == -1 ? "max" : maxLength.ToString())})";
        if (name is "decimal" or "numeric")
            return $"{name}({precision},{scale})";
        if (name is "datetime2" or "datetimeoffset" or "time")
            return $"{name}({scale})";
        return name;
    }
}
