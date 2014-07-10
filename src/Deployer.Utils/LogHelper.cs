using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CK.Core;
using Deployer.Settings;

namespace Deployer.Utils
{
    public static class LogHelper
    {
        public static TextWriter ReplicateIn( IActivityMonitor logger, ISettings settings, string directoryName, string filename )
        {
            if( !string.IsNullOrEmpty( settings.LogDirectory ) )
            {
                string backupLogDirectory = Path.Combine( settings.LogDirectory, directoryName );

                if( !Directory.Exists( backupLogDirectory ) )
                    Directory.CreateDirectory( backupLogDirectory );

                string logFilePath = Path.Combine( backupLogDirectory, filename );

                TextWriter txtWr = File.CreateText( logFilePath );

                logger.Output.RegisterClient( new ActivityMonitorTextWriterClient( s => txtWr.Write( s ) ) );

                return txtWr;
            }
            return TextWriter.Null;
        }
    }
}
