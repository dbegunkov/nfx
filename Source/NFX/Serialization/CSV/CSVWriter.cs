﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using NFX.DataAccess;
using NFX.DataAccess.CRUD;

namespace NFX.Serialization.CSV
{
  public static class CSVWriter
  {
    public static void WriteToFile(RowsetBase rowset,
                                   string fileName,
                                   CSVWritingOptions options = null,
                                   Encoding encoding = null)
    {
      using(var fs = new FileStream(fileName, FileMode.Create, FileAccess.Write))
          Write(rowset, fs, options, encoding);
    }

    public static byte[] WriteToBuffer(RowsetBase rowset,
                                       CSVWritingOptions options = null,
                                       Encoding encoding = null)
    {
      using(var ms = new MemoryStream())
      {
        Write(rowset, ms, options, encoding);
        return ms.ToArray();
      }
    }

    public static void Write(RowsetBase rowset,
                             Stream stream,
                             CSVWritingOptions options = null,
                             Encoding encoding = null)
    {
      using (var wri = new StreamWriter(stream, encoding ?? UTF8Encoding.UTF8))
      {
        Write(rowset, wri, options);
      }
    }

    public static string Write(RowsetBase rowset,
                               CSVWritingOptions options = null)
    {
      var sb = new StringBuilder();
      using(var wri = new StringWriter(sb))
      {
        Write(rowset, wri, options);
      }

      return sb.ToString();
    }

    public static void Write(RowsetBase rowset, TextWriter wri, CSVWritingOptions options = null) 
    {
      if (rowset == null) return;

      if (options == null) options = CSVWritingOptions.Default;

      var defs = getAcceptableDefs(rowset.Schema, options.LoadAllFields);

      if (options.IncludeHeader) writeHeader(defs, wri, options);

      foreach(var item in rowset.AsReadonlyIList)
      {
        var row = item as Row;
        if (row == null) continue;

        writeRow(row, defs, wri, options);
      }
    }

    public static void WriteToFile(Row row,
                                   string fileName,
                                   CSVWritingOptions options = null,
                                   Encoding encoding = null)
    {
      using (var fs = new FileStream(fileName, FileMode.Create, FileAccess.Write))
      {
        Write(row, fs, options, encoding);
      }
    }

    public static byte[] WriteToBuffer(Row row,
                                       CSVWritingOptions options = null,
                                       Encoding encoding = null)
    {
      using(var ms = new MemoryStream())
      {
        Write(row, ms, options, encoding);
        return ms.ToArray();
      }
    }

    public static string Write(Row row,
                               CSVWritingOptions options = null)
    {
      var sb = new StringBuilder();
      using(var wri = new StringWriter(sb))
      {
        Write(row, wri, options);
      }

      return sb.ToString();
    }

    public static void Write(Row row,
                             Stream stream,
                             CSVWritingOptions options = null,
                             Encoding encoding = null)
    {
      using (var wri = new StreamWriter(stream, encoding ?? Encoding.UTF8))
      {
        Write(row, wri, options);
      }
    }

    public static void Write(Row row, TextWriter wri, CSVWritingOptions options = null)
    {
      if (row == null) return;

      if (options == null) options = CSVWritingOptions.Default;

      var defs = getAcceptableDefs(row.Schema, options.LoadAllFields);

      if (options.IncludeHeader) writeHeader(defs, wri, options);

      writeRow(row, defs, wri, options);
    }

    #region .pvt
      private static IEnumerable<Schema.FieldDef> getAcceptableDefs(Schema schema, bool allFields)
      {
        if (schema == null) 
          throw new NFXSerializationException("CSVWriter.getAcceptableDefs - schema is null");

        if (allFields) return schema.FieldDefs;

        var res = new List<Schema.FieldDef>();

        foreach(var def in schema.FieldDefs)
        {
          if (!isWritable(def)) continue;
          res.Add(def);
        }

        return res;
      }

      private static void writeHeader(IEnumerable<Schema.FieldDef> defs, TextWriter wri, CSVWritingOptions options)
      {
        var first = true;
        var newline = false;
        foreach(var def in defs)
        {
          if (!first) wri.Write(options.FieldDelimiter);
          wri.Write(def.Name);

          first = false;
          newline = true;
        }

        if (newline) wri.WriteLine();
      }

      private static void writeRow(Row row, IEnumerable<Schema.FieldDef> defs, TextWriter wri, CSVWritingOptions options)
      {
        var first = true;
        var newline = false;
        foreach(var def in defs)
        {
          if (!first) wri.Write(options.FieldDelimiter);
          var val = row.GetFieldValue(def);
          writeValue(val, wri, options);

          first = false;
          newline = true;
        }

        if (newline) wri.WriteLine();
      }

      private static bool isWritable(Schema.FieldDef def)
      {
        var attr = def[null];
        return !(attr.NonUI ||
                 attr.StoreFlag == StoreFlag.None ||
                 attr.StoreFlag == StoreFlag.OnlyLoad);
      }

      private static void writeValue(object value, TextWriter wri, CSVWritingOptions options)
      {
        if (value == null)
        {
          wri.Write(options.NullValue);
          return;
        }

        if (value is string)
        {
          wri.Write(escape((string)value, options.FieldDelimiter));
          return;
        }

        if (value is bool)
        {
          wri.Write(((bool)value) ? "true" : "false");
          return;
        }

        if (value is int || value is long)
        {
          wri.Write(((IConvertible)value).ToString(System.Globalization.CultureInfo.InvariantCulture));
          return;
        }

        if (value is double || value is float || value is decimal)
        {
          wri.Write(escape(((IConvertible)value).ToString(System.Globalization.CultureInfo.InvariantCulture),
                    options.FieldDelimiter));
          return;
        }

        if (value is DateTime)
        {
          wri.Write(((DateTime)value).ToString(System.Globalization.CultureInfo.InvariantCulture));
          return;
        }

        if (value is TimeSpan)
        {
          var ts = (TimeSpan)value;
          wri.Write(ts.Ticks);
          return;
        }

        wri.Write(escape(value.ToString(), options.FieldDelimiter));
      }

      private static string escape(string str, char del)
      {
        bool needsQuotes = str.IndexOfAny(new char[] {'\n', '\r', '"', del}) >= 0;

        str = str.Replace("\"", "\"\"");

        return needsQuotes ? "\"" + str + "\"" : str;
      }
    #endregion
  }
}