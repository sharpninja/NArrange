namespace NArrange.Core
{
    using System;
    using System.Collections.ObjectModel;
    using System.Globalization;
    using System.IO;
    using System.Reflection;
    using System.Text;

    using NArrange.Core.CodeElements;
    using NArrange.Core.Configuration;

    public class StringArranger
    {
        #region Fields

        /// <summary>
        /// Configuration file name supplied.
        /// </summary>
        private readonly string _configFile;

        /// <summary>
        /// Logger to write messages to.
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// The code arranger for arranging source files.
        /// </summary>
        private CodeArranger _codeArranger;

        /// <summary>
        /// Code configuration.
        /// </summary>
        private CodeConfiguration _configuration;

        /// <summary>
        /// Default file encoding.
        /// </summary>
        private Encoding _encoding;

        /// <summary>
        /// Project manager for retrieving solution/project/directory source files
        /// and the parsers associated with those files.
        /// </summary>
        private ProjectManager _projectManager;

        #endregion Fields

        #region Constructors

        /// <summary>
        /// Creates a new string arranger.
        /// </summary>
        /// <param name="configFile">The config file.</param>
        /// <param name="logger">The logger.</param>
        public StringArranger(string configFile, ILogger logger)
        {
            _configFile = configFile;
            _logger = logger;
        }

        #endregion Constructors

        #region Methods

        /// <summary>
        /// Arranges a file as a string
        /// </summary>
        /// <param name="inputFile">The input file.</param>
        /// <param name="inputText">The text of the file.</param>
        /// <param name="outputText">The arranged text of the file.</param>
        /// <returns>True if successful, otherwise false.</returns>
        public bool Arrange(string inputFile, string inputFileText, out string outputFileText)
        {
            outputFileText = null;

            if (!Initialize())
                return false;

            bool isProject = _projectManager.IsProject(inputFile);
            bool isSolution = !isProject && ProjectManager.IsSolution(inputFile);
            bool isDirectory = !isProject && !isSolution && string.IsNullOrEmpty(Path.GetExtension(inputFile)) && Directory.Exists(inputFile);

            if (isProject || isSolution || isDirectory)
                return false;

            if (File.Exists(String.Concat(inputFile, ".cs")))
            {
                inputFile = String.Concat(inputFile, ".cs");
            }
            else if (File.Exists(String.Concat(inputFile, ".vb")))
            {
                inputFile = String.Concat(inputFile, ".vb");
            }

            bool canParse = _projectManager.CanParse(inputFile);
            if (!canParse)
            {
                LogMessage(
                    LogLevel.Warning,
                    "No assembly is registered to handle file {0}.  Please update the configuration or select a valid file.",
                    inputFile);

                return false;
            }

            ReadOnlyCollection<ICodeElement> elements = null;

            Encoding encoding = _encoding;
            if (encoding == null)
                encoding = FileUtilities.GetEncoding(inputFile);

            try
            {
                elements = _projectManager.ParseElements(inputFile, inputFileText);
                LogMessage(LogLevel.Trace, "Parsed {0}", inputFile);
            }
            catch (ParseException parseException)
            {
                LogMessage(
                    LogLevel.Warning,
                    "Unable to parse file {0}: {1}",
                    inputFile,
                    parseException.Message);

                return false;
            }

            if (elements != null)
            {
                try
                {
                    if (_codeArranger == null)
                        _codeArranger = new CodeArranger(_configuration);

                    elements = _codeArranger.Arrange(elements);
                }
                catch (InvalidOperationException invalidEx)
                {
                    LogMessage(
                        LogLevel.Warning,
                        "Unable to arrange file {0}: {1}",
                       inputFile,
                       invalidEx.ToString());

                    return false;
                }
            }

            if (elements != null)
            {
                ICodeElementWriter codeWriter = _projectManager.GetSourceHandler(inputFile).CodeWriter;
                codeWriter.Configuration = _configuration;

                StringWriter writer = new StringWriter(CultureInfo.InvariantCulture);
                try
                {
                    codeWriter.Write(elements, writer);
                }
                catch (Exception ex)
                {
                    LogMessage(LogLevel.Error, ex.ToString());
                    return false;
                }

                outputFileText = writer.ToString();
            }

            return true;
        }

        /// <summary>
        /// Gets a configuration load error.
        /// </summary>
        /// <param name="filename">The filename.</param>
        /// <param name="ex">The exception.</param>
        /// <returns>The configuration load error text.</returns>
        private static string GetConfigurationLoadError(string filename, Exception ex)
        {
            StringBuilder messageBuilder = new StringBuilder(
                string.Format(
                CultureInfo.CurrentUICulture,
                "Unable to load configuration file {0}: {1}",
                filename,
                ex.Message));

            if (ex.InnerException != null)
            {
                messageBuilder.AppendFormat(" {0}", ex.InnerException.Message);
            }

            return messageBuilder.ToString();
        }

        /// <summary>
        /// Initializes this instance.
        /// </summary>
        /// <returns>True if successful, otherwise false.</returns>
        private bool Initialize()
        {
            bool success = true;

            try
            {
                LoadConfiguration(_configFile);
            }
            catch (InvalidOperationException xmlException)
            {
                string message = GetConfigurationLoadError(_configFile, xmlException);
                LogMessage(LogLevel.Error, message);
                success = false;
            }
            catch (IOException ioException)
            {
                string message = GetConfigurationLoadError(_configFile, ioException);
                LogMessage(LogLevel.Error, message);
                success = false;
            }
            catch (UnauthorizedAccessException authException)
            {
                string message = GetConfigurationLoadError(_configFile, authException);
                LogMessage(LogLevel.Error, message);
                success = false;
            }
            catch (TargetInvocationException invEx)
            {
                LogMessage(
                    LogLevel.Error,
                    "Unable to load extension assembly from configuration file {0}: {1}",
                    _configFile,
                    invEx.Message);
                success = false;
            }

            return success;
        }

        /// <summary>
        /// Loads the configuration file that specifies how elements will be arranged.
        /// </summary>
        /// <param name="configFile">The config file.</param>
        private void LoadConfiguration(string configFile)
        {
            if (_configuration == null)
            {
                if (configFile != null)
                {
                    _configuration = CodeConfiguration.Load(configFile);
                }
                else
                {
                    _configuration = CodeConfiguration.Default;
                }

                _projectManager = new ProjectManager(_configuration);
                _encoding = _configuration.Encoding.GetEncoding();
            }
        }

        /// <summary>
        /// Log a message.
        /// </summary>
        /// <param name="level">The level.</param>
        /// <param name="message">The message or message format.</param>
        /// <param name="args">The message format args.</param>
        private void LogMessage(LogLevel level, string message, params object[] args)
        {
            if (_logger != null)
            {
                _logger.LogMessage(level, message, args);
            }
        }

        #endregion Methods
    }
}