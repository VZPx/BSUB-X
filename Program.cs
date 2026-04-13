using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BSUB_X
{
    internal class Program
    {
        public static int subOffset = 0x608;
        static void Main(string[] args)
        {
            if (args.Length == 0)
            {
                Console.WriteLine("Usage:\nbsub-x.exe -arg1 -arg2 path_to_file.bsub\n\n" +
                    "Arg1: \n" + "   -u (Unpack into .bsubasset)\n" +
                    "   -p (Pack into .bsub)" + "\n\n" +
                    "Arg2 (Optional): \n     -r (Unpack in raw format)");
                return;
            }
            else if (args.Length > 1)
            {
                bool isRaw = false;
                if (args.Length > 2)
                {
                    isRaw = args[1].Trim() == "-r";
                }

                string arg1 = args[0].Trim();
                string filePath = @args.Last();

                switch (arg1)
                {
                    case "-u":
                        ExtractBSUB(filePath, isRaw);
                        break;
                    case "-p":
                        PackBSUB(filePath);
                        break;
                }
            }
        }

        static void ExtractBSUB(string filePath, bool rawExport)
        {
            StringBuilder sb = new StringBuilder();
            var entries = new List<SubtitleEntry>();

            using (BigEndianReader stream = new BigEndianReader(new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite)))
            {
                int currentEntry = 1;

                int startOffset = 0x08;

                stream.ReadBytes(startOffset); //skip the header

                while (true)
                {
                    byte[] entry = stream.ReadBytes(12);

                    using (MemoryStream ms = new MemoryStream(entry))
                    using (BigEndianReader entryReader = new BigEndianReader(ms))
                    {
                        SubtitleEntry subEntry = new SubtitleEntry
                        {
                            StartTime = entryReader.ReadInt32(),
                            EndTime = entryReader.ReadInt32(),
                            TextOffset = entryReader.ReadInt16(),
                            CharacterLength = entryReader.ReadInt16(),
                        };

                        if (subEntry.StartTime == 0 &&
                            subEntry.EndTime == 0)
                        {
                            break; //we're in the padding
                        }

                        entries.Add(subEntry);

                        if (rawExport)
                        {
                            sb.AppendLine($"[Entry {currentEntry}]\n" + subEntry.ToString());
                        }
                        currentEntry++;
                    }
                }
            }

            if (rawExport)
            {
                string text = $"Script: \n" + GetDialogueText(filePath);
                sb.AppendLine(text);
            }
            else
            {
                int entryNumber = 1;
                foreach (var entry in entries)
                {
                    sb.AppendLine($"[Entry {entryNumber}]");
                    sb.AppendLine($"Time: {entry.StartTime} - {entry.EndTime}");
                    sb.AppendLine("Text: " + 
                        GetDialogueTextAt(filePath, entry.TextOffset, entry.CharacterLength));
                    sb.AppendLine("");
                    entryNumber++;
                }
            }

            string fileName = Path.GetFileNameWithoutExtension(filePath);
            string finalPath = Path.Combine(Path.GetDirectoryName(filePath), fileName + ".bsubasset");
            File.WriteAllText(finalPath, sb.ToString());

            Console.WriteLine($"\n--- {(rawExport ? "Raw" : "")} Extraction Summary ---");
            Console.WriteLine($"File: {fileName}.bsub");
            Console.WriteLine($"Entries Found: {entries.Count}");
            Console.WriteLine($"Output: {Path.GetFileName(finalPath)}");
            Console.WriteLine("--------------------------\n");
        }

        static void PackBSUB(string filePath)
        {
            string[] lines = File.ReadAllLines(filePath);
            List<SubtitleEntry> newEntries = new List<SubtitleEntry>();
            string script = "";
            short textOffset = 0;
            bool isRaw = File.ReadAllText(filePath).Contains("Character Length");
            //Character Length is something only the raw bsubasset would have

            for (int i = 0; i < lines.Length; i++)
            {
                if (isRaw)
                {
                    if (lines[i].StartsWith("[Entry"))
                    {
                        var entry = new SubtitleEntry();
                        // Parse Start Time (Example: "Start Time: 3382 ms")
                        entry.StartTime = int.Parse(lines[i + 1].Split(':')[1].Replace("ms", "").Trim());
                        entry.EndTime = int.Parse(lines[i + 2].Split(':')[1].Replace("ms", "").Trim());
                        entry.TextOffset = short.Parse(lines[i + 3].Split(':')[1].Trim());
                        entry.CharacterLength = short.Parse(lines[i + 4].Split(':')[1].Trim());
                        newEntries.Add(entry);
                        i += 4; // Skip the lines we just read
                    }
                    else if (lines[i].StartsWith("Script:"))
                    {
                        // Join everything after "Script:" into one big string
                        script = string.Join("\n", lines.Skip(i + 1)).Trim();
                        break;
                    }
                }
                else
                {
                    if (lines[i].StartsWith("[Entry"))
                    {
                        var entry = new SubtitleEntry();

                        entry.StartTime = int.Parse(lines[i + 1].Split(':')[1].Split('-')[0].Trim());
                        entry.EndTime = int.Parse(lines[i + 1].Split(':')[1].Split('-')[1].Trim());

                        string text = lines[i + 2].Split(':')[1].Remove(0, 1); //remove whitespace

                        int nextLine = i + 3;
                        while (nextLine < lines.Length && !lines[nextLine].StartsWith("[Entry"))
                        {
                            string nextText = lines[nextLine];

                            if (!string.IsNullOrWhiteSpace(nextText))
                            {
                                text += "\n" + nextText;
                            }

                            nextLine++;
                        }

                        entry.TextOffset = textOffset;
                        entry.CharacterLength = (short)text.Length;

                        textOffset += entry.CharacterLength;
                        newEntries.Add(entry);

                        script += text;

                        i += 2; //skip lines we read
                    }
                }
            }

            //Console.WriteLine(script);

            string outPath = filePath.Replace(".bsubasset", ".bsub");
            using (FileStream fs = new FileStream(outPath, FileMode.Create))
            using (BigEndianWriter writer = new BigEndianWriter(fs))
            {
                // Write Header
                writer.Write(Encoding.ASCII.GetBytes("BSUB"));
                writer.Write(0); // Write padding

                // Write Index Table
                foreach (var sub in newEntries)
                {
                    writer.Write(sub.StartTime);
                    writer.Write(sub.EndTime);
                    writer.Write(sub.TextOffset);
                    writer.Write(sub.CharacterLength);
                }

                // Fill Padding until 0x608
                while (fs.Position < 0x608) { writer.Write((byte)0); }

                // Write Text
                byte[] textData = Encoding.BigEndianUnicode.GetBytes(script);
                writer.Write(textData);        
            }

            string fileName = Path.GetFileNameWithoutExtension(filePath);
            string finalPath = Path.Combine(Path.GetDirectoryName(filePath), fileName + ".bsub");

            Console.WriteLine("\n--- Pack Summary ---");
            Console.WriteLine($"File: {fileName}.bsub");
            Console.WriteLine($"Entries Found: {newEntries.Count}");
            Console.WriteLine($"Output: {Path.GetFileName(finalPath)}");
            Console.WriteLine("--------------------------\n");
        }

        static string GetDialogueTextAt(string filePath, short offset, short length)
        {
            string content = GetDialogueText(filePath);

            return content.Substring(offset, length);
        }

        static string GetDialogueText(string filePath)
        {
            using (BigEndianReader stream = new BigEndianReader(new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite)))
            {
                stream.ReadBytes(subOffset); //Subtitle text offset

                int remainingBytes = (int)stream.BaseStream.Length - subOffset;
                byte[] characters = stream.ReadBytes(remainingBytes);

                //characters = characters.ToList().Where(b => b != 0x00).ToArray();

                string clearText = Encoding.BigEndianUnicode.GetString(characters);

                return clearText;
            }
        }
    }
}

public class SubtitleEntry
{
    public int StartTime { get; set; }
    public int EndTime { get; set; }
    public short TextOffset { get; set; }
    public short CharacterLength { get; set; }
    public override string ToString()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"Start Time: {StartTime} ms");
        sb.AppendLine($"End Time: {EndTime} ms");
        sb.AppendLine($"Text Offset: {TextOffset}");
        sb.AppendLine($"Character Length: {CharacterLength}");
        return sb.ToString();
    }
}