using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Dalamud.Plugin;
using Ionic.Zip;
using Lumina.Data;
using Newtonsoft.Json;
using Penumbra.Importer.Models;
using Penumbra.Models;

namespace Penumbra.Importer
{
    internal class TexToolsImport
    {
        private readonly DirectoryInfo _outDirectory;

        public TexToolsImport( DirectoryInfo outDirectory )
        {
            _outDirectory = outDirectory;
        }

        public void ImportModPack( FileInfo modPackFile )
        {
            switch( modPackFile.Extension )
            {
                case ".ttmp":
                    ImportV1ModPack( modPackFile );
                    return;

                case ".ttmp2":
                    ImportV2ModPack( modPackFile );
                    return;
            }
        }

        private void ImportV1ModPack( FileInfo modPackFile )
        {
            PluginLog.Log( "    -> Importing V1 ModPack" );

            using var extractedModPack = ZipFile.Read( modPackFile.OpenRead() );

            var modListRaw = GetStringFromZipEntry( extractedModPack[ "TTMPL.mpl" ], Encoding.UTF8 ).Split(
                new[] { "\r\n", "\r", "\n" },
                StringSplitOptions.None
            );

            var modList = modListRaw.Select( JsonConvert.DeserializeObject< SimpleMod > );

            // Create a new ModMeta from the TTMP modlist info
            var modMeta = new ModMeta
            {
                Author = "Unknown",
                Name = modPackFile.Name,
                Description = "Mod imported from TexTools mod pack"
            };

            // Open the mod data file from the modpack as a SqPackStream
            var modData = GetSqPackStreamFromZipEntry( extractedModPack[ "TTMPD.mpd" ] );

            var newModFolder = new DirectoryInfo( Path.Combine( _outDirectory.FullName,
                Path.GetFileNameWithoutExtension( modPackFile.Name ) ) );
            newModFolder.Create();

            File.WriteAllText( Path.Combine( newModFolder.FullName, "meta.json" ),
                JsonConvert.SerializeObject( modMeta ) );

            ExtractSimpleModList( newModFolder, modList, modData );
        }

        private void ImportV2ModPack( FileInfo modPackFile )
        {
            using var extractedModPack = ZipFile.Read( modPackFile.OpenRead() );

            var modList =
                JsonConvert.DeserializeObject< SimpleModPack >( GetStringFromZipEntry( extractedModPack[ "TTMPL.mpl" ],
                    Encoding.UTF8 ) );

            if( modList.TTMPVersion.EndsWith( "s" ) )
            {
                ImportSimpleV2ModPack( extractedModPack );
                return;
            }

            if( modList.TTMPVersion.EndsWith( "w" ) )
            {
                ImportExtendedV2ModPack( extractedModPack );
            }
        }

        private void ImportSimpleV2ModPack( ZipFile extractedModPack )
        {
            PluginLog.Log( "    -> Importing Simple V2 ModPack" );

            var modList =
                JsonConvert.DeserializeObject< SimpleModPack >( GetStringFromZipEntry( extractedModPack[ "TTMPL.mpl" ],
                    Encoding.UTF8 ) );

            // Create a new ModMeta from the TTMP modlist info
            var modMeta = new ModMeta
            {
                Author = modList.Author,
                Name = modList.Name,
                Description = string.IsNullOrEmpty( modList.Description )
                    ? "Mod imported from TexTools mod pack"
                    : modList.Description
            };

            // Open the mod data file from the modpack as a SqPackStream
            var modData = GetSqPackStreamFromZipEntry( extractedModPack[ "TTMPD.mpd" ] );

            var newModFolder = new DirectoryInfo( Path.Combine( _outDirectory.FullName,
                Path.GetFileNameWithoutExtension( modList.Name ) ) );
            newModFolder.Create();

            File.WriteAllText( Path.Combine( newModFolder.FullName, "meta.json" ),
                JsonConvert.SerializeObject( modMeta ) );

            ExtractSimpleModList( newModFolder, modList.SimpleModsList, modData );
        }

        private void ImportExtendedV2ModPack( ZipFile extractedModPack )
        {
            PluginLog.Log( "    -> Importing Extended V2 ModPack" );

            var modList =
                JsonConvert.DeserializeObject< ExtendedModPack >( GetStringFromZipEntry( extractedModPack[ "TTMPL.mpl" ],
                    Encoding.UTF8 ) );

            // Create a new ModMeta from the TTMP modlist info
            var modMeta = new ModMeta
            {
                Author = modList.Author,
                Name = modList.Name,
                Description = string.IsNullOrEmpty( modList.Description )
                    ? "Mod imported from TexTools mod pack"
                    : modList.Description
            };

            // Open the mod data file from the modpack as a SqPackStream
            var modData = GetSqPackStreamFromZipEntry( extractedModPack[ "TTMPD.mpd" ] );

            var newModFolder = new DirectoryInfo( Path.Combine( _outDirectory.FullName,
                Path.GetFileNameWithoutExtension( modList.Name ) ) );
            newModFolder.Create();

            File.WriteAllText( Path.Combine( newModFolder.FullName, "meta.json" ),
                JsonConvert.SerializeObject( modMeta ) );

            if( modList.SimpleModsList != null )
                ExtractSimpleModList( newModFolder, modList.SimpleModsList, modData );

            if( modList.ModPackPages == null )
                return;

            // Iterate through all pages
            // For now, we are just going to import the default selections
            // TODO: implement such a system in resrep?
            foreach( var option in from modPackPage in modList.ModPackPages
                from modGroup in modPackPage.ModGroups
                from option in modGroup.OptionList
                where option.IsChecked
                select option )
            {
                ExtractSimpleModList( newModFolder, option.ModsJsons, modData );
            }
        }

        private void ImportMetaModPack( FileInfo file )
        {
            throw new NotImplementedException();
        }

        private void ExtractSimpleModList( DirectoryInfo outDirectory, IEnumerable< SimpleMod > mods, SqPackStream dataStream )
        {
            // Extract each SimpleMod into the new mod folder
            foreach( var simpleMod in mods )
            {
                if( simpleMod == null )
                    continue;

                ExtractMod( outDirectory, simpleMod, dataStream );
            }
        }

        private void ExtractMod( DirectoryInfo outDirectory, SimpleMod mod, SqPackStream dataStream )
        {
            PluginLog.Log( "        -> Extracting {0} at {1}", mod.FullPath, mod.ModOffset.ToString( "X" ) );

            try
            {
                var data = dataStream.ReadFile< FileResource >( mod.ModOffset );

                var extractedFile = new FileInfo( Path.Combine( outDirectory.FullName, mod.FullPath ) );
                extractedFile.Directory?.Create();

                File.WriteAllBytes( extractedFile.FullName, data.Data );
            }
            catch( Exception ex )
            {
                PluginLog.LogError( ex, "Could not export mod." );
            }
        }

        private static MemoryStream GetStreamFromZipEntry( ZipEntry entry )
        {
            var stream = new MemoryStream();
            entry.Extract( stream );
            return stream;
        }

        private static string GetStringFromZipEntry( ZipEntry entry, Encoding encoding )
        {
            return encoding.GetString( GetStreamFromZipEntry( entry ).ToArray() );
        }

        private static SqPackStream GetSqPackStreamFromZipEntry( ZipEntry entry )
        {
            return new SqPackStream( GetStreamFromZipEntry( entry ) );
        }
    }
}