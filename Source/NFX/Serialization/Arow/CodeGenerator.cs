﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Reflection;

using NFX.Environment;
using NFX.DataAccess.CRUD;

namespace NFX.Serialization.Arow
{
  /// <summary>
  /// Generates code for serilaizer and deserializer
  /// </summary>
  public class CodeGenerator : DisposableObject
  {
    /// <summary>
    /// Defines how generated files should be stored on disk
    /// </summary>
    public enum GeneratedCodeSegregation
    {
      FilePerNamespace = 0,
      FilePerType,
      AllInOne
    }

    [Config]
    public string RootPath{ get; set;}

    [Config]
    public GeneratedCodeSegregation CodeSegregation{ get; set;}



    public void Generate(Assembly asm)
    {
      if (asm==null) throw new ArowException(StringConsts.ARGUMENT_ERROR.Args("Generate(asm==null)"));
      if (!Directory.Exists(RootPath)) throw new ArowException(StringConsts.AROW_GENERATOR_PATH_DOESNOT_EXIST_ERROR.Args(RootPath));
      DoGenerate(asm);
    }

    protected virtual void DoGenerate(Assembly asm)
    {
      var types = GetRowTypes(asm);
      string ns = null;
      string tn = null;
      StringBuilder source = null;
      foreach(var trow in types)
      {
        if (source==null ||
            CodeSegregation==GeneratedCodeSegregation.FilePerType ||
            (CodeSegregation==GeneratedCodeSegregation.FilePerNamespace && trow.Namespace!=ns))
        {
          EmitFileFooter(source);
          WriteContent(ns, tn, source);
          source = new StringBuilder();
          EmitFileHeader(source);
        }

        if (trow.Namespace!=ns)
        {
          if (ns.IsNotNullOrWhiteSpace()) EmitNamespaceFooter(source);
          EmitNamespaceHeader(source, trow.Namespace);
        }

        ns = trow.Namespace;
        tn = trow.Name;
        EmitITypeSerializationCore(source, trow);
      }
      EmitNamespaceFooter(source);
      EmitFileFooter(source);
      WriteContent(ns, tn, source);
    }

    protected virtual void WriteContent(string ns, string name, StringBuilder content)
    {
      if (content==null || content.Length==0 || name.IsNullOrWhiteSpace()) return;

      if (ns!=null)
        ns = ns.Replace('.', Path.DirectorySeparatorChar);

      if (CodeSegregation!= GeneratedCodeSegregation.FilePerType) name = "ArowTypes";

      var path = Path.Combine(this.RootPath, ns);
      NFX.IOMiscUtils.EnsureAccessibleDirectory(path);

      var fn = Path.Combine(path, name+".cs");

      File.WriteAllText(fn, content.ToString());
    }

    protected virtual IEnumerable<Type> GetRowTypes(Assembly asm)
    {
      var types = asm.GetTypes().Where( t => t.IsClass && Attribute.IsDefined(t, typeof(ArowAttribute), false) && typeof(TypedRow).IsAssignableFrom(t))
                                .OrderBy( t => t.Namespace )
                                .ToArray();
      return types;
    }

    /// <summary>
    /// Converts backend name of up to 8 ASCII chars in length
    /// </summary>
    public static ulong GetName(string name)
    {
      ulong result = 0;
      if (name.IsNullOrWhiteSpace()) return 0;
      var sl = name.Length;
      if (sl>sizeof(ulong)) throw new ArowException(StringConsts.AROW_INVALID_FIELD_NAME_ERROR.Args(name, sizeof(ulong)));
      for(var i=0; i<sizeof(ulong) && i<name.Length; i++)
      {
        result <<= 8;
        var c = name[i];
        if (c>0xff) throw new ArowException(StringConsts.AROW_INVALID_FIELD_NAME_ERROR.Args(name, sizeof(ulong)));
        result |= (byte)c;
      }
      return result;
    }

    /// <summary>
    /// Converts backend name of up to 8 ASCII chars in length
    /// </summary>
    public static string GetName(ulong name)
    {
      var result = "";

      for(var i=0; i<sizeof(ulong); i++)
      {
        var c = name & 0xff;
        if (c==0) return result;
        result = ((char)c) + result;
        name >>= 8;
      }
      return result;
    }


    protected virtual void EmitFileHeader(StringBuilder source)
    {
      var bi = NFX.Environment.BuildInformation.ForFramework;
      source.AppendLine("// Do not mofify by hand. This file is auto-generated by Arow generator");
      source.AppendLine("// Generated on {0} by {1} at {2}".Args(DateTime.Now, System.Environment.UserName, System.Environment.MachineName));
      source.AppendLine("// Framework: " + bi.ToString());
      source.AppendLine();
      source.AppendLine("using System;");
      source.AppendLine("using System.Collections.Generic;");
      source.AppendLine("using NFX;");
      source.AppendLine("using NFX.IO;");
      source.AppendLine("using NFX.DataAccess.CRUD;");
      source.AppendLine("using NFX.Serialization.Arow;");
      source.AppendLine("using AW = NFX.Serialization.Arow.Writer;");
      source.AppendLine();
    }

    protected virtual void EmitNamespaceHeader(StringBuilder source, string ns)
    {
      if (source==null) return;
      source.AppendLine("namespace {0}.Arow".Args(ns));
      source.AppendLine("{");
    }

    protected virtual void EmitNamespaceFooter(StringBuilder source)
    {
      if (source==null) return;
      source.AppendLine("}//namespace");
      source.AppendLine();
    }

    protected virtual void EmitFileFooter(StringBuilder source)
    {
      if (source==null) return;
      source.AppendLine();
      source.AppendLine("//EOF");
    }

    protected virtual void EmitITypeSerializationCore(StringBuilder source, Type tRow)
    {
      if (source==null) return;
      var cname = "{0}_arow_core".Args(tRow.Name);
      source.AppendLine("  ///<summary>");
      source.AppendLine("  /// ITypeSerializationCore for {0}".Args(tRow.FullName));
      source.AppendLine("  ///</summary>");
      source.AppendLine("  internal class {0} : NFX.Serialization.Arow.ITypeSerializationCore".Args(cname));
      source.AppendLine("  {");
      var schema = Schema.GetForTypedRow(tRow);

      source.AppendLine("    void ITypeSerializationCore.Register()");
      source.AppendLine("    {");
      source.AppendLine("       ArowSerializer.Register(typeof({0}), this);".Args(tRow.FullName));
      source.AppendLine("    }");
      source.AppendLine();

      EmitSerialize(source, schema);
      source.AppendLine();

      EmitDeserialize(source, schema);

      source.AppendLine("  }//class");
      source.AppendLine();
    }


    protected virtual void EmitSerialize(StringBuilder source, Schema schema)
    {
      source.AppendLine("    void ITypeSerializationCore.Serialize(TypedRow aRow, WritingStreamer streamer)");
      source.AppendLine("    {");
      source.AppendLine("      var row = ({0})aRow;".Args(schema.TypedRowType.FullName));
         EmitSerializeBody(source, schema);
      source.AppendLine("    }");
    }

    protected virtual void EmitSerializeBody(StringBuilder source, Schema schema)
    {
      foreach(var def in schema)
        EmitSerializeFieldLine(source, def);
    }

    protected virtual void EmitSerializeFieldLine(StringBuilder source, Schema.FieldDef fdef)
    {
      if (source==null) return;
      var fatr = fdef.Attrs.FirstOrDefault( a => a.IsArow);
      if (fatr==null) return;//do not serilize this field
      var name = GetName(fatr.BackendName);
      if (name==0) return;//no name specified

      var isValueType = fdef.Type.IsValueType;
      var isNullable = isValueType ? fdef.NonNullableType!=fdef.Type : false;

      var propertyName = fdef.MemberInfo.Name;
      var propertyValue = propertyName;
      if (isNullable) propertyValue = "{0}.Value".Args(propertyValue);

      source.AppendLine("      // '{0}' = {1}".Args(fatr.BackendName, name));

      if (isNullable)
       source.AppendLine("      if (row.{0}.HasValue)".Args(propertyName));
      else if (!isValueType)
       source.AppendLine("      if (row.{0} != null)".Args(propertyName));

      var t = fdef.NonNullableType;
      string expr;
      if (!Writer.SER_TYPE_MAP.TryGetValue(t, out expr))
      {
        if (t.IsEnum)
         source.AppendLine("      Writer.Write(streamer, {0}, (int)row.{1});".Args(name, propertyName));
        else
        if (typeof(TypedRow).IsAssignableFrom(t))
         source.AppendLine("      Writer.WriteRow(streamer, {0}, row.{1});".Args(name, propertyName));
        else
        if (t.IsArray && typeof(TypedRow).IsAssignableFrom(t.GetElementType()))
         source.AppendLine("      Writer.WriteRowArray(streamer, {0}, row.{1});".Args(name, propertyName));
        else
        if (t.IsGenericType && t.GetGenericTypeDefinition()==typeof(List<>) && typeof(TypedRow).IsAssignableFrom(t.GetGenericArguments()[0]))
         source.AppendLine("      Writer.WriteRowArray(streamer, {0}, row.{1});".Args(name, propertyName));
        else
        throw new ArowException(StringConsts.AROW_MEMBER_TYPE_NOT_SUPPORTED_ERROR.Args(t.Name));

        if (isNullable || !isValueType)
          source.AppendLine("      else AW.WriteNull(streamer, {0});".Args(name));
        return;
      }

      if (expr.IsNullOrWhiteSpace()) expr = "row.{0}";

      source.AppendLine("      AW.Write(streamer, {0}, {1});".Args(name, expr.Args( propertyValue )) );

      if (isNullable || !isValueType)
       source.AppendLine("      else AW.WriteNull(streamer, {0});".Args(name));
    }



    protected virtual void EmitDeserialize(StringBuilder source, Schema schema)
    {
      source.AppendLine("    void ITypeSerializationCore.Deserialize(TypedRow aRow, ReadingStreamer streamer)");
      source.AppendLine("    {");
      source.AppendLine("      var row = ({0})aRow;".Args(schema.TypedRowType.FullName));
         EmitDeserializeBody(source, schema);
      source.AppendLine("    }");
    }

    protected virtual void EmitDeserializeBody(StringBuilder source, Schema schema)
    {
      source.AppendLine("      while(true)");
      source.AppendLine("      {");

      source.AppendLine("         var name = Reader.ReadName(streamer);");
      source.AppendLine("         if (name==0) break;//EORow");
      source.AppendLine("         var dt = Reader.ReadDataType(streamer);");
      source.AppendLine("         DataType? atp = null;");
      source.AppendLine("         switch(name)");
      source.AppendLine("         {");
      foreach(var def in schema)
      {
        var fatr = def.Attrs.FirstOrDefault( a => a.IsArow);
        if (fatr==null) continue;
        var name = GetName(fatr.BackendName);
        if (name==0) continue;//no name specified

        source.AppendLine("           case {0}: {{ // '{1}'".Args(name, fatr.BackendName));

        EmitDeserializeField(source, def);

        source.AppendLine("                     }");//case label
      }

      //source.AppendLine("             default: break;");
      source.AppendLine("         }");
      source.AppendLine("         Reader.ConsumeUnmatched(row, streamer, CodeGenerator.GetName(name), dt, atp);");
      source.AppendLine("      }");

    }

    protected virtual void EmitDeserializeField(StringBuilder source, Schema.FieldDef fdef)
    {
      var t = fdef.Type;
      var isNullable = t.IsGenericType && t.GetGenericTypeDefinition() == typeof(Nullable<>);
      var isValueType = t.IsValueType;
      var isArray = t.IsArray;
      var isList = t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>);
      var prop = fdef.MemberInfo.Name;

      var tcName = t.FullNameWithExpandedGenericArgs().Replace('+','.');

      //Specialized handling via dict
      string code;
      if (!Reader.DESER_TYPE_MAP.TryGetValue(t, out code))
      {
        if (isNullable || !isValueType)
          source.AppendLine("           if (dt==DataType.Null) {{ row.{0} = null; continue;}} ".Args(prop));

        if (t.IsEnum)
        {
          source.AppendLine("           if (dt!=DataType.Int32) break;");
          source.AppendLine("           var ev = ({0})Reader.ReadInt32(streamer);".Args(tcName));
          source.AppendLine("           row.{0} = ev;".Args(prop));
          source.AppendLine("           continue;");
          return;
        } else if (typeof(TypedRow).IsAssignableFrom(t))
        {
          source.AppendLine("           if (dt!=DataType.Row) break;");
          source.AppendLine("           var vrow = new {0}();".Args(t.FullName));
          source.AppendLine("           if (Reader.TryReadRow(row, vrow, streamer, CodeGenerator.GetName(name)))");
          source.AppendLine("             row.{0} = vrow;".Args(prop));
          source.AppendLine("           continue;");
          return;
        } else if (isArray)
        {
          var et = t.GetElementType();
          if(typeof(TypedRow).IsAssignableFrom(et))
          {
            source.AppendLine("           if (dt!=DataType.Array) break;");
            source.AppendLine("           atp = Reader.ReadDataType(streamer);");
            source.AppendLine("           if (atp!=DataType.Row) break;");
            source.AppendLine("           row.{0} = Reader.ReadRowArray<{1}>(row, streamer, CodeGenerator.GetName(name));".Args(prop, et.FullName));
            source.AppendLine("           continue;");
            return;
          }
        } else if (isList)
        {
          var gat = t.GetGenericArguments()[0];
          if(typeof(TypedRow).IsAssignableFrom(gat))
          {
            source.AppendLine("           if (dt!=DataType.Array) break;");
            source.AppendLine("           atp = Reader.ReadDataType(streamer);");
            source.AppendLine("           if (atp!=DataType.Row) break;");
            source.AppendLine("           row.{0} = Reader.ReadRowList<{1}>(row, streamer, CodeGenerator.GetName(name));".Args(prop, gat.FullName));
            source.AppendLine("           continue;");
            return;
          }
        }

      }

      if (code.IsNotNullOrWhiteSpace())
      {
         source.AppendLine( code.Args(fdef.MemberInfo.Name) );
         return;
      }

      //Generate
      if (isArray)
      {
        var et = t.GetElementType();
        source.AppendLine("           if (dt==DataType.Null) row.{0} = null;".Args(prop));
        source.AppendLine("           else if (dt!=DataType.Array) break;");
        source.AppendLine("           else");
        source.AppendLine("           {");
        source.AppendLine("              atp = Reader.ReadDataType(streamer);");
        source.AppendLine("              if (atp!=DataType.{0}) break;".Args(et.Name));
        source.AppendLine("              var len = Reader.ReadArrayLength(streamer);");
        source.AppendLine("              var arr = new {0}[len];".Args(et.FullName));
        source.AppendLine("              for(var i=0; i<len; i++) arr[i] = Reader.Read{0}(streamer);".Args(et.Name));
        source.AppendLine("              row.{0} = arr;".Args(prop));
        source.AppendLine("           }");
        source.AppendLine("           continue;");
      } else if (isList)
      {
        var gat = t.GetGenericArguments()[0];
        var tn = gat.Name;
        source.AppendLine("           if (dt==DataType.Null) row.{0} = null;".Args(prop));
        source.AppendLine("           if (dt!=DataType.Array) break;");
        source.AppendLine("           else");
        source.AppendLine("           {");
        source.AppendLine("              atp = Reader.ReadDataType(streamer);");
        source.AppendLine("              if (atp!=DataType.{0}) break;".Args(tn));
        source.AppendLine("              var len = Reader.ReadArrayLength(streamer);");
        source.AppendLine("              var lst = new List<{0}>(len);".Args(gat.FullName));
        source.AppendLine("              for(var i=0; i<len; i++) lst.Add( Reader.Read{0}(streamer) );".Args(tn));
        source.AppendLine("              row.{0} = lst;".Args(prop));
        source.AppendLine("           }");
        source.AppendLine("           continue;");
      } else if (isNullable || t==typeof(string))
      {
        var tn = fdef.NonNullableType.Name;
        source.AppendLine("           if (dt==DataType.Null) row.{0} = null;".Args(prop));
        source.AppendLine("           else if (dt==DataType.{1}) row.{0} = Reader.Read{1}(streamer);".Args(prop, tn));
        source.AppendLine("           else break;");
        source.AppendLine("           continue;");
      }
      else //regular
      {
        var tn = fdef.NonNullableType.Name;
        source.AppendLine("           if (dt==DataType.{1}) row.{0} = Reader.Read{1}(streamer);".Args(prop, tn));
        source.AppendLine("           else break;");
        source.AppendLine("           continue;");
      }
    }


  }
}
