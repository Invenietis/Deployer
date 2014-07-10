using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using CK.Core;
using Deployer.Action;
using Deployer.Actions;
using Deployer.Utils;

namespace Deployer
{
    class Program
    {
        static void Main( string[] args )
        {
            Console.BufferWidth = 1024;

            ActivityMonitor logger = new ActivityMonitor();
            logger.Output.RegisterClient( new ActivityMonitorConsoleClient() );

            Runner runner = new Runner( logger );
            DiscoverAndRegisterActions( runner, logger );

            runner.Run( args );
        }

        static void DiscoverAndRegisterActions( Runner runner, IActivityMonitor logger )
        {
            Assembly assemblyToProcess = typeof( SettingsConfigurator ).Assembly;

            foreach( var type in assemblyToProcess.GetTypes() )
            {
                if( typeof( IAction ).IsAssignableFrom( type ) )
                {
                    if( type.Namespace != "Deployer.Actions" )
                        logger.Warn().Send( "The action {0} should be defined in \"Deployer.Actions\" namespace instead of {1}", type.Name, type.Namespace );

                    runner.RegisterAction( (IAction)Activator.CreateInstance( type ) );
                }
            }
        }
    }
}
