using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using System.Linq;
using Dapper;
using ExifLibrary;
using MetadataExtractor;

namespace Util01
{
	internal class Program
	{	// FIXME: - connection string needs updating for SQL server 2019 instance
		private static readonly string ConnectionString = @"data source=(localdb)\ProjectsV13;initial catalog=pops;integrated security=True;MultipleActiveResultSets=True";
		private static readonly StreamWriter _writer = File.AppendText(@"./logfile.txt");

		// List of Checksum rows where filenames are the same
		private static List<CheckSum> _checksums = new List<CheckSum>();

		private static int Main(string[] args)
		{

			// Uses System.CommandLine beta library
			// see https://github.com/dotnet/command-line-api/wiki/Your-first-app-with-System.CommandLine
			// PM> Install-Package System.CommandLine -Version 2.0.0-beta1.20104.2

			RootCommand rootCommand = new RootCommand("DupesMaintConsole")
				{
					new Option("--folder", "The root folder of the tree to scan which must exist, 'F:/Picasa backup/c/photos'.")
						{
							Argument = new Argument<DirectoryInfo>().ExistingOnly(),
							Required = true
						},

					new Option("--replace", "Replace default (true) or append (false) to the db tables CheckSum & CheckSumDupes.")
						{
							Argument = new Argument<bool>(getDefaultValue: () => true),
							Required = false
						}

				};

			// sub command to extract EXIF date/time from all JPG image files in a folder tree
			#region "subcommand EXIF"
			var command2 = new Command("EXIF")
			{
				new Option("--folder", "The root folder to scan image file, 'C:\\Users\\User\\OneDrive\\Photos")
					{
						Argument = new Argument<DirectoryInfo>().ExistingOnly(),
						Required = true,
					},

				new Option("--replace", "Replace default (true) or append (false) to the db tables CheckSum.")
					{
						Argument = new Argument<bool>(getDefaultValue: () => true),
						Required = true,
					}
			};

			command2.Handler = CommandHandler.Create((DirectoryInfo folder, bool replace) => { ProcessEXIF(folder, replace); });
			rootCommand.AddCommand(command2);
			#endregion

			#region "subcommand anEXIF"
			var command3 = new Command("anEXIF")
			{
				new Option("--image", "An image file, 'C:\\Users\\User\\OneDrive\\Photos\\2013\\02\\2013-02-24 12.34.54-3.jpg'")
					{
						Argument = new Argument<FileInfo>().ExistingOnly(),
						Required = true,
					}

			};

			command3.Handler = CommandHandler.Create((FileInfo image) => { ProcessAnEXIF(image); });
			rootCommand.AddCommand(command3);
			#endregion


			// setup the root command handler
			rootCommand.Handler = CommandHandler.Create((DirectoryInfo folder, bool replace) => { Process(folder, replace); });

			// call the method defined in the handler
			return rootCommand.InvokeAsync(args).Result;
		}

		// subCommand3
		private static void ProcessAnEXIF(FileInfo image)
		{
			IEnumerable<MetadataExtractor.Directory> directories = ImageMetadataReader.ReadMetadata(image.FullName);

			string mess = $"{ DateTime.Now}, ProcessAnEXIF, INFO - image: {image.FullName}";
			Log(mess);

			foreach (var _directory in directories)
			{
				foreach (var tag in _directory.Tags)
				{
					mess = $"[{_directory.Name}] - [{tag.Name}] = [{tag.Description}]";
					Log(mess);
				}
			}

		}

		private static void ProcessEXIF(DirectoryInfo folder, bool replace)
		{
			int _count = 0;
			string mess = $"\r\n{DateTime.Now}, INFO - ProcessEXIF: target folder is {folder.FullName}\n\rTruncate CheckSum is: {replace}.";
			Log(mess);
			Console.WriteLine($"{DateTime.Now}, INFO - press any key to start processing.");
			Console.ReadLine();

			if (replace)
			{
				using (IDbConnection db = new SqlConnection(ConnectionString))
				{
					db.Execute("truncate table dbo.CheckSum");
				}
			}

			// get an array of FileInfo objects from the folder tree
			FileInfo[] _files = folder.GetFiles("*.JPG", SearchOption.AllDirectories);

			foreach (FileInfo fi in _files)
			{
				// get the EXIF date/time 
				(DateTime _CreateDateTime, string _sCreateDateTime) = ImageEXIF(fi);


				// instantiate a new CheckSum object for the file
				var checkSum = new CheckSum
				{
					SHA = "",
					Folder = fi.DirectoryName,
					TheFileName = fi.Name,
					FileExt = fi.Extension,
					FileSize = (int)fi.Length,
					FileCreateDt = fi.CreationTime,
					CreateDateTime = _CreateDateTime,
					SCreateDateTime = _sCreateDateTime
				};

				// insert into DB table
				CheckSum_ins2(checkSum);


				_count++;

				if (_count % 1000 == 0)
				{
					mess = $"{DateTime.Now}, INFO - {_count.ToString("N0")}. Completed: {((_count * 100) / _files.Length)}%. Processing folder: {fi.DirectoryName}";
					Log(mess);
				}
			}

		}

		private static (DateTime CreateDateTime, string sCreateDateTime) ImageEXIF(FileInfo fileInfo)
		{
			DateTime _CreateDateTime = new DateTime(1753, 1, 1);
			string _sCreateDateTime = "Date not found";
			ImageFile _file;
			string mess;

			// try to convert the file into a EXIF ImageFile
			try
			{
				_file = ImageFile.FromFile(fileInfo.FullName);
			}
			catch (NotValidImageFileException)
			{
				_sCreateDateTime = "Not valid image";
				mess = $"{DateTime.Now}, WARN - File: {fileInfo.FullName}, _sCreateDateTime: {_sCreateDateTime}, _CreateDateTime: {_CreateDateTime}";
				Log(mess);

				return (CreateDateTime: _CreateDateTime, sCreateDateTime: _sCreateDateTime);
			}
			catch (Exception exc)
			{
				_sCreateDateTime = "ERROR";
				mess = $"{DateTime.Now}, ERR - File: {fileInfo.FullName}\r\n{exc.ToString()}\r\n";
				Log(mess);

				return (CreateDateTime: _CreateDateTime, sCreateDateTime: _sCreateDateTime);
			}

			ExifDateTime _dateTag = _file.Properties.Get<ExifDateTime>(ExifTag.DateTime);


			if (_dateTag != null)
			{
				_sCreateDateTime = _dateTag.ToString();
				if (DateTime.TryParse(_sCreateDateTime, out _CreateDateTime))
				{
					if (_CreateDateTime == DateTime.MinValue)
					{
						_CreateDateTime = new DateTime(1753, 1, 1);
					}
				}
			}

			return (CreateDateTime: _CreateDateTime, sCreateDateTime: _sCreateDateTime);
		}


		public static void LogImageEXIF(IEnumerable<MetadataExtractor.Directory> directory, FileInfo fileInfo)
		{
			Console.WriteLine($"\r\nfileInfo: {fileInfo.FullName}");

			foreach (var _directory in directory)
			{
				if (_directory.Name.Equals("Exif SubIFD"))
				{
					foreach (var tag in _directory.Tags)
					{
						Console.WriteLine($"[{_directory.Name}] - [{tag.Name}] = [{tag.Description}]");
					}
				}
			}

		}

		public static void Process(DirectoryInfo folder, bool replace)
		{
			Console.WriteLine($"{DateTime.Now}, INFO - target folder is {folder.FullName}\n\rTruncate CheckSum is: {replace}.");
			Console.WriteLine($"{DateTime.Now}, INFO - press any key to start processing.");
			Console.ReadLine();
			System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();

			if (replace)
			{
				using (IDbConnection db = new SqlConnection(ConnectionString))
				{
					db.Execute("truncate table dbo.CheckSum");
				}
			}

			// main processing
			int fileCount = ProcessFiles(folder);

			_stopwatch.Stop();
			Console.WriteLine($"{DateTime.Now}, Total execution time: {_stopwatch.ElapsedMilliseconds / 60000} mins. # of files processed: {fileCount}.");
		}


		private static int ProcessFiles(DirectoryInfo folder)
		{
			int _count = 0;

			System.Diagnostics.Stopwatch process100Watch = System.Diagnostics.Stopwatch.StartNew();

			FileInfo[] _files = folder.GetFiles("*", SearchOption.AllDirectories);

			// Process all the files in the source directory tree
			foreach (FileInfo fi in _files)
			{
				// instantiate a new CheckSum object for the file
				var checkSum = new CheckSum
				{
					SHA = "",
					Folder = fi.DirectoryName,
					TheFileName = fi.Name,
					FileExt = fi.Extension,
					FileSize = (int)fi.Length / 1024,
					FileCreateDt = fi.CreationTime
				};

				// see if the file name already exists in the Checksums list
				var alreadyExists = _checksums.Find(x => x.TheFileName == fi.Name);

				// if the file name already exists then write the Checksum and the new Checksum to the CheckSum table is the DB
				if (alreadyExists != null)
				{
					CheckSum_ins(alreadyExists);
					CheckSum_ins(checkSum);
				}
				else // just add the new file to the CheckSum list
				{
					_checksums.Add(checkSum);
				}

				_count++;

				if (_count % 1000 == 0)
				{
					process100Watch.Stop();
					Console.WriteLine($"{DateTime.Now}, INFO - {_count}. Last 100 in {process100Watch.ElapsedMilliseconds / 1000} secs. " +
						$"Completed: {(_count * 100) / _files.Length}%. " +
						$"Processing folder: {fi.DirectoryName}");
					process100Watch.Reset();
					process100Watch.Start();
				}
			}
			return _count;
		}


		private static void CheckSum_ins(CheckSum checkSum)
		{
			// create the SqlParameters for the stored procedure
			var p = new DynamicParameters();
			p.Add("@SHA", checkSum.SHA);
			p.Add("@Folder", checkSum.Folder);
			p.Add("@TheFileName", checkSum.TheFileName);
			p.Add("@FileExt", checkSum.FileExt);
			p.Add("@FileSize", checkSum.FileSize);
			p.Add("@FileCreateDt", checkSum.FileCreateDt);
			p.Add("@TimerMs", checkSum.TimerMs);
			p.Add("@Notes", "");

			// call the stored procedure
			using (IDbConnection db = new SqlConnection(ConnectionString))
			{
				db.Execute("dbo.spCheckSum_ins", p, commandType: CommandType.StoredProcedure);
			}

		}

		private static void CheckSum_ins2(CheckSum checkSum)
		{
			// create the SqlParameters for the stored procedure
			var p = new DynamicParameters();
			p.Add("@SHA", checkSum.SHA);
			p.Add("@Folder", checkSum.Folder);
			p.Add("@TheFileName", checkSum.TheFileName);
			p.Add("@FileExt", checkSum.FileExt);
			p.Add("@FileSize", checkSum.FileSize);
			p.Add("@FileCreateDt", checkSum.FileCreateDt);
			p.Add("@TimerMs", checkSum.TimerMs);
			p.Add("@Notes", "");
			p.Add("@CreateDateTime", checkSum.CreateDateTime);
			p.Add("@SCreateDateTime", checkSum.SCreateDateTime);

			// call the stored procedure
			using (IDbConnection db = new SqlConnection(ConnectionString))
			{
				db.Execute("dbo.spCheckSum_ins2", p, commandType: CommandType.StoredProcedure);
			}

		}
		private static void Log(string mess)
		{
			Console.WriteLine(mess);
			_writer.WriteLine(mess);
			_writer.Flush();
		}

	}
}
