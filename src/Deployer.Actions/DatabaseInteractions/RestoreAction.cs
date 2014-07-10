using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using CK.Core;
using Deployer.Action;
using Deployer.Settings;
using Deployer.Utils;
using Mono.Options;

namespace Deployer.Actions
{
    public class RestoreAction : IAction
    {
        string _baseName;
        SpecificBackup _specific;

        public string Description
        {
            get { return "Restore the last backup file to the configured database"; }
        }

        public IEnumerable<SubOptions> GetSubOptions()
        {
            return new SubOptions[]{
                new SubOptions(){ ArgumentName = "--from=", Description=@"Restore the last backup file with a specific name."}
            };
        }

        public ISettings LoadSettings( ISettingsLoader loader, IList<string> extraParameters, IActivityMonitor logger )
        {
            return ConfigHelper.TryLoadCustomPathOrDefault( loader, extraParameters, logger );
        }

        public void CheckSettingsValidity( ISettings settings, IList<string> extraParameters, IActivityMonitor logger )
        {
            // Check connection string
            if( !string.IsNullOrEmpty( settings.ConnectionString ) )
            {
                DatabaseHelper.TryToConnectToDB( settings.ConnectionString, logger );
            }
            else logger.Error().Send( "No connection string configured" );

            // Check backup directory
            if( !string.IsNullOrEmpty( settings.BackupDirectory ) )
            {
                if( Directory.Exists( Path.GetFullPath( settings.BackupDirectory ) ) )
                {
                    if( !Directory.EnumerateFiles( Path.GetFullPath( settings.BackupDirectory ), "*.bak" ).Any()
                        && !Directory.EnumerateFiles( Path.GetFullPath( Path.Combine( settings.BackupDirectory, "WithSpecificNames" ) ) ).Any() )
                        logger.Error().Send( "The backup directory is empty" );
                }
                else logger.Error().Send( "The backup directory does not exist" );
            }
            else logger.Error().Send( "No backup directory configured" );

            string parsedBaseName = null;
            var options = new OptionSet() { { "from=", v => parsedBaseName = v } };

            try
            {
                options.Parse( extraParameters );
            }
            catch( Exception ex )
            {
                logger.Error().Send( "Error while parsing extra parameters" );
                logger.Error().Send( ex );
            }

            _baseName = null;
            _specific = null;
            if( !string.IsNullOrWhiteSpace( parsedBaseName ) )
            {
                _baseName = parsedBaseName;
                _specific = new SpecificBackup( settings, _baseName );
                if( _specific.BackupFile == null )
                    logger.Error().Send( "Unable to find a backup named as '{0}'", _baseName );
            }
        }

        public void Run( Runner runner, ISettings settings, IList<string> extraParameters, IActivityMonitor logger )
        {
            string formatedDate = DateTime.Now.ToFileFormatString();

            FileInfo backupFile = null;
            string backupDirectoryPath = _baseName != null ? Path.Combine( settings.BackupDirectory, "WithSpecificNames" ) : settings.BackupDirectory;
            DirectoryInfo backupDirectory = new DirectoryInfo( Path.GetFullPath( backupDirectoryPath ) );

            int innerErrorCount = 0;
            using( logger.CatchCounter( ( errorCount ) => innerErrorCount = errorCount ) )
            {
                if( _baseName != null )
                {
                    backupFile = _specific.BackupFile;
                }
                else // find the last backup
                {
                    using( logger.OpenInfo().Send( "Looking for the last written backup file" ) )
                    {
                        using( logger.OpenInfo().Send( "Available backup files" ) )
                        {
                            foreach( var bak in backupDirectory.EnumerateFiles( "*.bak" ) )
                            {
                                logger.Info().Send( bak.Name );
                                if( backupFile == null || bak.LastWriteTimeUtc > backupFile.LastWriteTimeUtc )
                                    backupFile = bak;
                            }
                        }
                        if( backupFile == null )
                            logger.Error().Send( "Unable to find a backup file to use" );
                    }
                }
            }

            if( innerErrorCount == 0 )
            {
                using( logger.OpenWarn().Send( "Backup file found. Here are some details :" ) )
                {
                    logger.Warn().Send( "Filename : {0}", backupFile.Name );
                    logger.Warn().Send( "Creation date : {0}", backupFile.CreationTime );
                    logger.Warn().Send( "Size : {0} mo", backupFile.Length / 1024 / 1024 );
                }

                if( CommandLineHelper.PromptBool( string.Format( "Are you sure you want to restore your database with the backup file {0} ? This cannot be undone !", backupFile.Name ) ) )
                {
                    using( LogHelper.ReplicateIn( logger, settings, "Restores", string.Concat( "Restore-", formatedDate, ".log" ) ) )
                    {
                        using( SqlConnection conn = new SqlConnection( settings.ConnectionString ) )
                        {
                            try
                            {
                                conn.Open();
                                conn.InfoMessage += ( o, e ) =>
                                {
                                    logger.Info().Send( e.Message );
                                };

                                using( StreamReader sr = new StreamReader( Assembly.GetExecutingAssembly().GetManifestResourceStream( "Deployer.Actions.DatabaseInteractions.RestoreFormat.sql" ) ) )
                                {
                                    string sqlFile = sr.ReadToEnd();
                                    using( var cmd = conn.CreateCommand() )
                                    {
                                        cmd.CommandText = string.Format( sqlFile, conn.Database, Path.Combine( Path.GetFullPath( settings.BackupDirectory ), backupFile.FullName ) );
                                        using( logger.OpenInfo().Send( "Starting restore of {0}", conn.Database ) )
                                        {
                                            cmd.ExecuteNonQuery();
                                        }

                                        logger.Info().Send( "Restore finished" );
                                    }
                                }
                            }
                            catch( Exception ex )
                            {
                                logger.Error().Send( ex, "Unable to restore the database." );
                            }
                        }
                    }
                }
                else
                {
                    logger.Info().Send( "Restore aborted" );
                }
            }
        }
    }
}
