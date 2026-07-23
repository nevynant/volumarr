using System;
using NzbDrone.Common.EnvironmentInfo;

namespace NzbDrone.Common.Exceptions
{
    public class SonarrStartupException : NzbDroneException
    {
        public SonarrStartupException(string message, params object[] args)
            : base($"{BuildInfo.AppName} failed to start: " + string.Format(message, args))
        {
        }

        public SonarrStartupException(string message)
            : base($"{BuildInfo.AppName} failed to start: " + message)
        {
        }

        public SonarrStartupException()
            : base($"{BuildInfo.AppName} failed to start")
        {
        }

        public SonarrStartupException(Exception innerException, string message, params object[] args)
            : base($"{BuildInfo.AppName} failed to start: " + string.Format(message, args), innerException)
        {
        }

        public SonarrStartupException(Exception innerException, string message)
            : base($"{BuildInfo.AppName} failed to start: " + message, innerException)
        {
        }

        public SonarrStartupException(Exception innerException)
            : base($"{BuildInfo.AppName} failed to start: " + innerException.Message)
        {
        }
    }
}
