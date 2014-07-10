﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Serialization;
using CK.Core;

namespace Deployer.Settings.Impl
{
    public class XmlSettingsLoader : ISettingsLoader
    {
        public const string DefaultConfigurationFileName = "Deployer.config";

        public ISettings Load( string filePath, IActivityMonitor logger )
        {
            using( logger.OpenInfo().Send( "Loading configuration" ) )
            {
                if( string.IsNullOrEmpty( filePath ) && File.Exists( DefaultConfigurationFileName ) )
                    filePath = DefaultConfigurationFileName;

                if( !string.IsNullOrEmpty( filePath ) )
                {
                    logger.Info().Send( "Configuration file found at {0}", Path.GetFullPath( filePath ) );
                    return LoadFromFile( filePath );
                }
                return new XmlSettings();
            }
        }

        public void Save( ISettings settings )
        {
            XmlSettings xmlSettings = settings as XmlSettings;
            if( xmlSettings == null )
                throw new ArgumentException( "XmlSettingsLoader is not able to save other settings than XmlSettings." );

            if( string.IsNullOrEmpty( xmlSettings.FilePath ) )
                xmlSettings.FilePath = DefaultConfigurationFileName;

            if( File.Exists( xmlSettings.FilePath ) )
                File.Delete( xmlSettings.FilePath );

            using( Stream fileStream = File.OpenWrite( xmlSettings.FilePath ) )
            {
                XmlSerializer x = new XmlSerializer( typeof( XmlSettings ) );
                x.Serialize( fileStream, xmlSettings );
            }
        }

        XmlSettings LoadFromFile( string filePath )
        {
            using( Stream fileStream = File.OpenRead( filePath ) )
            {
                if( fileStream.Length > 0 )
                {
                    XmlSerializer x = new XmlSerializer( typeof( XmlSettings ) );
                    XmlSettings s = (XmlSettings)x.Deserialize( fileStream );
                    s.FilePath = filePath;
                    return s;
                }
                else
                    return new XmlSettings();
            }
        }
    }
}
