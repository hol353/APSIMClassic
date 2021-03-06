using System;
using System.Collections.Generic;
using System.Text;
using System.Data;
using System.Xml;
using ApsimFile;
using CSGeneral;
using System.IO;
#if __MonoCS__
using Mono.Data.Sqlite;
#else
using System.Data.SQLite;
#endif
using System.Data.OleDb;
using System.Data.Common;

public class DataProcessor
{
    // -------------------------------------------------
    // This class takes a XML description of a series
    // of data processing commands. These are then used
    // to calculate and return a DataTable to caller.
    // e.g of XML:
    //   <GDProbability>
    //     <GDApsimFileReader>
    //       <FileName>test1.out</FileName>
    //       <FileName>test2.out</FileName>
    //     </GDApsimFileReader>
    //   </GDProbability>
    // This XML returns a probability distribution of
    // source data from a nested ApsimFileReader.
    // -------------------------------------------------

    public DataTable Go(string Xml)
    {
        // -------------------------------------------------
        // Use the XML passed in to calculate and return a 
        // datatable. 
        // -------------------------------------------------
        XmlDocument Doc = new XmlDocument();
        Doc.LoadXml(Xml);
        return GetDataTable(Doc.DocumentElement);
    }
    public DataTable Go(XmlNode Data, string NodePath)
    {
        // -------------------------------------------------
        // Similar to the previous method, this one uses the 
        // XML passed in (as an XMLNode) to calculate and 
        // return a datatable. 
        // -------------------------------------------------
        DataTable Table = GetDataTable(Data);
        if (Table == null)
            Table = GoFindChildDataTable(Data);

        return Table;
    }


    public DataTable GoFindChildDataTable(XmlNode Data)
    {
        // -------------------------------------------------
        // Used by many of the processors below to go find
        // a nested source DataTable under the specified
        // XMLNode.
        // -------------------------------------------------
        DataTable Table;
        foreach (XmlNode Child in Data.ChildNodes)
        {
            Table = GetDataTable(Child);
            if (Table != null)
                return Table;
        }
        return null;
    }
    public List<DataTable> GoFindChildDataTables(XmlNode Data)
    {
        // -------------------------------------------------
        // Used by many of the processors below to go find
        // a nested source DataTable under the specified
        // XMLNode.
        // -------------------------------------------------
        List<DataTable> Tables = new List<DataTable>();
        foreach (XmlNode Child in Data.ChildNodes)
        {
            DataTable Table = GetDataTable(Child);
            if (Table != null)
                Tables.Add(Table);
        }
        return Tables;
    }
    private DataTable GetDataTable(XmlNode Node)
    {
        // -------------------------------------------------
        // Try and return a DataTable for the specified node.
        // If the node isn't recognised then null is returned.
        // -------------------------------------------------
        DataTable Data = null;
        if (Node.Name.ToLower() == "gdapsimfilereader")
            Data = ProcessApsimFileReader(Node);
        else if (Node.Name.ToLower() == "gdprobability")
            Data = ProcessProbability(Node);
        else if (Node.Name.ToLower() == "gdsoi")
            Data = ProcessSOI(Node);
        else if (Node.Name.ToLower() == "gddepth")
            Data = ProcessDepth(Node);
        else if (Node.Name.ToLower() == "gdpredobs")
            Data = ProcessPredObs(Node);
        else if (Node.Name.ToLower() == "gdfilter")
            Data = ProcessFilter(Node);
        else if (Node.Name.ToLower() == "reportdb")
            Data = ProcessReportDb(Node);
        else if (Node.Name.ToLower() == "rems")
            Data = REMS(Node);
        if (Data != null)
            Data.TableName = XmlHelper.Name(Node);
        return Data;
    }


    #region Default file name functionality
    private List<string> DefaultFileNames = new List<string>();
    public List<string> DefaultOutputFileNames
    {
        // -------------------------------------------------
        // If an ApsimFileReader has a blank <filename>
        // element then these default file names will be
        // used.
        // -------------------------------------------------
        get { return DefaultFileNames; }
        set
        {
            DefaultFileNames = value;
        }
    }
    #endregion

    #region GroupBy methods
    private List<string> TitleFilters = null;
    private int TitleFilterNumber;
    public bool GroupByTitle(DataView Data)
    {
        // -------------------------------------------------
        // Used by the data processors to group the Data
        // into 'series'. The 'title' column is used to 
        // group the data. This method is called repeatedly
        // until it returns false.
        // e.g.
        //    col1   col2   col3    title
        //      10      ?     20    TestFile1
        //      11      5     21    TestFile1
        //       ?      6     22    TestFile1
        //     100     10    120    TestFile2
        //     112     25    121    TestFile2
        //     116     46    122    TestFile2
        // A first call to this method with this data will
        // return all data for title = TestFile1. A second
        // call will return all data for title = TestFile2.
        // A third call will return false.
        // -------------------------------------------------

        if (Data.RowFilter == "")
        {
            TitleFilters = DataTableUtility.GetDistinctValues(Data.Table, "title");
            TitleFilterNumber = 0;
        }
        else
        {
            TitleFilterNumber++;
            if (TitleFilterNumber >= TitleFilters.Count)
            {
                Data.RowFilter = "";
                return false;
            }
        }
        if (TitleFilters.Count == 0)
            return false;

        Data.RowFilter = "Title = '" + TitleFilters[TitleFilterNumber] + "'";
        return true;
    }
    #endregion

    #region Data Processors
    private DataTable ProcessApsimFileReader(XmlNode Node)
    {
        // -------------------------------------------------
        // The XmlNode is a GDApsimFileReader so go read
        // a series of APSIM output files and return a
        // DataTable with all data.
        // -------------------------------------------------

        List<string> FileNames = XmlHelper.Values(Node, "FileName");
        if (FileNames.Count == 0)
            FileNames.AddRange(DefaultFileNames);

        DataTable Data = null;
        foreach (string FileSpec in FileNames)
        {
            if (FileSpec != "")
            {
                string FileSpecNoMacros = Configuration.RemoveMacros(FileSpec);
                string Dir = Path.GetDirectoryName(FileSpecNoMacros);
                if (Dir == "")
                    Dir = Directory.GetCurrentDirectory();

                foreach (string FileName in Directory.GetFiles(Dir,
                                                               Path.GetFileName(FileSpecNoMacros)))
                {
                    if (FileName != "" && File.Exists(FileName))
                    {
                        string CheckPointFile = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(FileName)), "CheckPoint", Path.GetFileName(FileName));
                        if (File.Exists(FileName))
                        {
                            APSIMInputFile InFile = new APSIMInputFile();
                            InFile.Open(FileName);
                            DataTable FileData = InFile.ToTable();
                            InFile.Close();
                            InFile.AddConstantsToData(FileData);
                            if (Data == null)
                                Data = FileData;
                            else
                                Merge(FileData, Data);
                        }
                        if (File.Exists(CheckPointFile))
                        {
                            APSIMInputFile InFile = new APSIMInputFile();
                            InFile.Open(CheckPointFile);
                            DataTable FileData = InFile.ToTable();
                            InFile.Close();
                            InFile.SetConstant("title", "{Checkpoint} " + InFile.Constant("title").Value);
                            InFile.AddConstantsToData(FileData);
                            if (Data == null)
                                Data = FileData;
                            else
                                Merge(FileData, Data);
                        }
                    }
                }
            }
        }
        return Data;
    }
    private DataTable ProcessProbability(XmlNode Node)
    {
        // -------------------------------------------------
        // The XmlNode is a GDProbability so go find a 
        // nested source DataTable, add a probability column
        // to it and return it to caller
        // -------------------------------------------------
        DataTable Data = GoFindChildDataTable(Node);
        if (Data == null || Data.Columns.Count == 0)
            return null;

        DataTable NewData = new DataTable();

        bool Exceedence = (XmlHelper.Value(Node, "exceedence").ToLower() == "yes");
        string ProbabilityColumnName;
        if (Exceedence)
            ProbabilityColumnName = "Probability of being above";
        else
            ProbabilityColumnName = "Probability of being below";

        // copy fields from old datatable to new datatable and add new probability column.
        NewData.Columns.Add(ProbabilityColumnName, Type.GetType("System.Single"));
        foreach (DataColumn Col in Data.Columns)
            NewData.Columns.Add(Col.ColumnName, Col.DataType);


        DataView View = new DataView();
        View.Table = Data;
        while (GroupByTitle(View))
        {
            double[] Probability = MathUtility.ProbabilityDistribution(View.Count, Exceedence);

            int StartRow = NewData.Rows.Count;
            for (int Col = 0; Col < Data.Columns.Count; Col++)
            {
                View.Sort = Data.Columns[Col].ColumnName;
                for (int Row = 0; Row < View.Count; Row++)
                {
                    if (Col == 0)
                    {
                        DataRow NewRow = NewData.NewRow();
                        NewRow[0] = Probability[Row];
                        NewData.Rows.Add(NewRow);
                    }
                    NewData.Rows[StartRow + Row][Col + 1] = View[Row][Col];
                }
            }
        }

        return NewData;
    }
    private DataTable ProcessSOI(XmlNode Node)
    {
        // -------------------------------------------------
        // The XmlNode is a GDSOI so go find a 
        // nested source DataTable, add an 'SOI Phase' column
        // to it and return it to caller.
        // -------------------------------------------------
        string[] DefaultPhaseNames = { "Unknown", "Negative", "Positive", "Falling", "Rising", "Zero" };

        // Go find the name of the soi phase file.
        string PhaseFile = XmlHelper.Value(Node, "PhaseFile");
        if (PhaseFile == "")
            PhaseFile = Configuration.Instance.ClimateSetting("SOIFile");
        if (PhaseFile == "" || !File.Exists(PhaseFile))
            return null;

        // Go find the SOI month we're to use for the lookup.
        int Month = Convert.ToInt32(XmlHelper.Value(Node, "Month"));

        // Work out the name of the year column.

        // Go find source data to work with
        DataTable Data = GoFindChildDataTable(Node);
        if (Data == null || Data.Columns.Count == 0)
            return null;

        // Add new SOI Phase column to Data.
        Data.Columns.Add("SOI Phase", Type.GetType("System.String"));

        // read in soi phase data.
        APSIMInputFile SOI = new APSIMInputFile();
        SOI.Open(PhaseFile);
        DataTable SOIData = SOI.ToTable();
        SOI.Close();

        int NumRows = Data.Rows.Count;
        for (int Row = 0; Row < NumRows; Row++)
        {
            string RowTitle = Data.Rows[Row]["Title"].ToString();

            DateTime RowDate = DataTableUtility.GetDateFromRow(Data.Rows[Row]);

            DataRow[] MatchingSOIData = SOIData.Select("Year = " + RowDate.Year.ToString() +
                                                       " and Month = " + Month.ToString());
            string PhaseName = "Unknown";
            if (MatchingSOIData.Length == 1)
            {
                if (SOIData.Columns.IndexOf("PhaseName") == -1)
                {
                    int PhaseNumber = Convert.ToInt32(MatchingSOIData[0]["Phase"]);
                    if (PhaseNumber < DefaultPhaseNames.Length)
                        PhaseName = DefaultPhaseNames[PhaseNumber];
                }
                else
                    PhaseName = MatchingSOIData[0]["PhaseName"].ToString();

            }
            Data.Rows[Row]["SOI Phase"] = PhaseName;
            Data.Rows[Row]["Title"] = RowTitle + ", " + PhaseName;

            // We also want to make a duplicate of this row and label it 'AllYears'
            Data.ImportRow(Data.Rows[Row]);
            Data.Rows[Data.Rows.Count - 1]["SOI Phase"] = "AllYears";
            Data.Rows[Data.Rows.Count - 1]["Title"] = RowTitle + ", AllYears";
        }
        return Data;
    }
    private DataTable ProcessDepth(XmlNode Node)
    {
        // -------------------------------------------------
        // The XmlNode is a GDDepth so go find a 
        // nested source DataTable, add transpose the data
        // so that depth is a column and the depth variables
        // are also columns.
        // -------------------------------------------------
        DataTable Data = GoFindChildDataTable(Node);
        if (Data == null || Data.Columns.Count == 0)
            return null;

        DataTable NewData = new DataTable();

        // copy profile fields from old datatable to new datatable and add new depth column.
        List<string> ProfileColumns = new List<string>();
        NewData.Columns.Add("Date", Type.GetType("System.DateTime"));
        NewData.Columns.Add("Depth", Type.GetType("System.Single"));
        int NumLayers = 0;
        foreach (DataColumn Col in Data.Columns)
        {
            string ColumnName = Col.ColumnName;
            string ArraySpecString = StringManip.SplitOffBracketedValue(ref ColumnName, '(', ')');
            int ArraySpec = 0;
            if (ArraySpecString != "")
                ArraySpec = Convert.ToInt32(ArraySpecString);
            if (ArraySpec == 1)
            {
                if (ColumnName.ToLower() != "dlayer")
                {
                    ProfileColumns.Add(ColumnName);
                    NewData.Columns.Add(ColumnName, Type.GetType("System.Single"));
                }
            }
            NumLayers = Math.Max(NumLayers, ArraySpec);
        }
        NewData.Columns.Add("Title", Type.GetType("System.String"));

        // Get a list of dates that we are to filter on.
        List<string> DateStrings = XmlHelper.Values(Node, "date");
        DateTime[] Dates = new DateTime[DateStrings.Count];
        for (int i = 0; i < DateStrings.Count; i++)
            Dates[i] = DateTime.ParseExact(DateStrings[i], "d/M/yyyy", null);

        for (int Row = 0; Row < Data.Rows.Count; Row++)
        {
            DateTime RowDate = DataTableUtility.GetDateFromRow(Data.Rows[Row]);
            if (Dates.Length == 0 || Array.IndexOf(Dates, RowDate) != -1)
            {
                double DepthSoFar = 0;
                for (int Layer = 1; Layer <= NumLayers; Layer++)
                {
                    double PreviousDepth = DepthSoFar;
                    DataRow NewRow = NewData.NewRow();
                    DepthSoFar += Convert.ToDouble(Data.Rows[Row]["dlayer(" + Layer.ToString() + ")"]);
                    NewRow["Depth"] = (DepthSoFar + PreviousDepth) / 2;

                    for (int Col = 0; Col < ProfileColumns.Count; Col++)
                    {
                        string ProfileColumnName = ProfileColumns[Col] + "(" + Layer.ToString() + ")";
                        if (Data.Columns.Contains(ProfileColumnName))
                            NewRow[ProfileColumns[Col]] = Data.Rows[Row][ProfileColumnName];
                    }
                    NewRow["Date"] = RowDate;
                    NewRow["Title"] = Data.Rows[Row]["Title"] + ", " + RowDate.ToShortDateString();
                    NewData.Rows.Add(NewRow);
                }
            }
        }
        return NewData;
    }

    private DataTable ProcessPredObs(XmlNode Node)
    {
        // -------------------------------------------------
        // The XmlNode is a GDPredObs so go find 2
        // nested source DataTables, it assumes one is called
        // predicted and the other observed.
        // -------------------------------------------------
        List<DataTable> Tables = GoFindChildDataTables(Node);
        if (Tables.Count == 2)
        {
            DataTable Predicted = null;
            DataTable Observed = null;
            if (Tables[0].TableName.ToLower() == "predicted")
                Predicted = Tables[0];
            else if (Tables[0].TableName.ToLower() == "observed")
                Observed = Tables[0];
            if (Tables[1].TableName.ToLower() == "predicted")
                Predicted = Tables[1];
            else if (Tables[1].TableName.ToLower() == "observed")
                Observed = Tables[1];

            if (Predicted != null && Observed != null)
            {
                List<string> FieldsToMatch = XmlHelper.Values(Node, "FieldsToMatch");

                DataTable NewData = new DataTable();
                NewData.Columns.Add("Title", Type.GetType("System.String"));
                NewData.Columns.Add("SeriesName", Type.GetType("System.String"));
                NewData.Columns.Add("PointName", Type.GetType("System.String"));

                // Add the necessary columns to our new dataset.
                foreach (DataColumn Column in Predicted.Columns)
                {
                    if (Column.ColumnName != "Title" && Observed.Columns.IndexOf(Column.ColumnName) != -1)
                    {
                        NewData.Columns.Add("Predicted " + Column.ColumnName, Column.DataType);
                        NewData.Columns.Add("Observed " + Column.ColumnName, Column.DataType);
                    }
                }

                foreach (DataRow ObservedRow in Observed.Rows)
                {
                    string Filter = "";
                    foreach (string FieldName in FieldsToMatch)
                    {
                        if (Filter != "")
                            Filter += " and ";
                        if (Observed.Columns[FieldName].DataType == Type.GetType("System.DateTime"))
                        {
                            DateTime D = Convert.ToDateTime(ObservedRow[FieldName]);
                            Filter += FieldName + " = #" + D.ToString("MM/dd/yyyy") + "#";
                        }
                        else if (Observed.Columns[FieldName].DataType == Type.GetType("System.String"))
                            Filter += FieldName + " = '" + ObservedRow[FieldName].ToString() + "'";
                        else
                            Filter += FieldName + " = " + ObservedRow[FieldName].ToString();
                    }
                    // Handle pred/obs for checkpointed series.
                    try
                    {
                        DataRow[] PredictedMatch;
                        if (Filter.ToLower().Contains("title"))
                        {
                            string CheckPointedFilter = Filter.Replace("Title = '", "Title = '{Checkpoint} ");
                            CheckPointedFilter = CheckPointedFilter.Replace("title = '", "title = '{Checkpoint} ");
                            PredictedMatch = Predicted.Select(CheckPointedFilter);
                            if (PredictedMatch.Length > 0)
                                WritePredictObservedRow(PredictedMatch, ObservedRow, NewData, "{Checkpoint} Predicted/Observed", FieldsToMatch);
                        }
                        // Handle pred/obs for noncheckpointed series.
                        PredictedMatch = Predicted.Select(Filter);
                        WritePredictObservedRow(PredictedMatch, ObservedRow, NewData, "Predicted/Observed", FieldsToMatch);
                    }
                    catch (Exception)
                    { }
                }
                return NewData;
            }

        }
        return null;
    }

    private void WritePredictObservedRow(DataRow[] PredictedMatch, DataRow ObservedRow, DataTable NewData, string Title, List<string> FieldsToMatch)
    {
        // find the corresponding row in predicted.

        DataRow NewRow = NewData.NewRow();
        NewRow["Title"] = Title;
        NewRow["SeriesName"] = ObservedRow["Title"];
        if (PredictedMatch.Length == 1)
        {
            foreach (DataColumn Column in PredictedMatch[0].Table.Columns)
            {
                int ObservedIndex = ObservedRow.Table.Columns.IndexOf(Column.ColumnName);
                if (Column.ColumnName != "Title" && ObservedIndex != -1 && !Convert.IsDBNull(ObservedRow[Column.ColumnName]))
                {
                    NewRow["Predicted " + Column.ColumnName] = PredictedMatch[0][Column.ColumnName];
                    NewRow["Observed " + Column.ColumnName] = ObservedRow[Column.ColumnName];
                }
            }
            // Create a point name
            string PointName = "";
            if (Title.Contains("{Checkpoint} "))
                PointName = "{Checkpoint} ";
            foreach (string FieldName in FieldsToMatch)
            {
                if (PointName != "")
                    PointName += ", ";
                if (ObservedRow.Table.Columns[FieldName].DataType == Type.GetType("System.DateTime"))
                {
                    DateTime D = Convert.ToDateTime(ObservedRow[FieldName]);
                    PointName += D.ToShortDateString();
                }
                else
                    PointName += ObservedRow[FieldName].ToString();
            }
            NewRow["PointName"] = PointName;
        }
        NewData.Rows.Add(NewRow);
    }
    private DataTable ProcessFilter(XmlNode Node)
    {
        // -------------------------------------------------
        // The XmlNode is a GDFilter so go filter a
        // nested source DataTable.
        // -------------------------------------------------

        string FilterString = XmlHelper.Value(Node, "filter");

        DataTable Data = GoFindChildDataTable(Node);
        if (Data == null || Data.Columns.Count == 0)
            return null;

        DataTable NewData = new DataTable();

        // copy all fields from old datatable to new datatable.
        foreach (DataColumn Col in Data.Columns)
            NewData.Columns.Add(Col.ColumnName, Col.DataType);

        // copy all fields.
        try
        {
            foreach (DataRow Row in Data.Select(FilterString))
            {
                DataRow NewRow = NewData.NewRow();

                for (int ColIndex = 0; ColIndex < Data.Columns.Count; ColIndex++)
                    NewRow[ColIndex] = Row[ColIndex];

                NewData.Rows.Add(NewRow);
            }
            return NewData;
        }
        catch (Exception e)
        {
            System.Windows.Forms.MessageBox.Show("Error in filter expression:\n" + e.Message);
            return null;
        }
    }

    private DataTable ProcessReportDb(XmlNode Node)
    {
        if (DefaultFileNames.Count > 0 && File.Exists(DefaultFileNames[0]))
        {
#if __MonoCS__
            SqliteConnection sql_con = new SqliteConnection("Data Source=Output.db;Version=3;New=False;Compress=True;");
#else
            SQLiteConnection sql_con = new SQLiteConnection("Data Source=" + DefaultFileNames[0] + ";Version=3;New=False;Compress=True;");
#endif
            sql_con.Open();
            string SQL = "SELECT Title, Data.* FROM Simulations, Data WHERE Simulations.ID = Data.SimulationID";
#if __MonoCS__
            SqliteDataAdapter DB = new SqliteDataAdapter(SQL, sql_con);
#else
            SQLiteDataAdapter DB = new SQLiteDataAdapter(SQL, sql_con);
#endif
            DataSet DS = new DataSet();
            DB.Fill(DS);
            return DS.Tables[0];
        }
        else
            return null;
    }

    private DataTable REMS(XmlNode Node)
    {
        string FileName = XmlHelper.Value(Node, "FileName");
        string Experiment = XmlHelper.Value(Node, "Experiment");
        string Treatment = XmlHelper.Value(Node, "Treatment");
        string DataSource = XmlHelper.Value(Node, "DataSource");
        if (File.Exists(FileName) && Experiment != "" && Treatment != "" && DataSource != "")
        {
            OleDbConnection con = null;
            try
            {
                string provider = "Provider=Microsoft.Jet.OLEDB.4.0;Data Source=" + FileName + ";Persist Security Info=False";
                con = new OleDbConnection(provider);
                con.Open();

                string SQL = null;
                SQL = "SELECT Experiments.Experiment, Treatments.TreatmentName, Treatments.TreatmentID " +
                      "FROM Experiments INNER JOIN Treatments ON Experiments.ExpID = Treatments.ExpID " +
                      "WHERE (((Experiments.Experiment)=\"" + Experiment + "\") AND ((Treatments.TreatmentName)=\"" + Treatment + "\"))";

                OleDbCommand cmd = new OleDbCommand(SQL, con); 
                OleDbDataReader reader = cmd.ExecuteReader();
                string TreatmentID;
                if (reader.Read()) // if can read row from database
                {
                    TreatmentID = reader.GetValue(2).ToString();
                }
                else
                    return null;

                if (DataSource == "Plot")
                {
                    SQL = "TRANSFORM Min(PlotData.Value) AS MinOfValue " +
                           "SELECT Plots.TreatmentID, PlotData.Date, Plots.Rep " +
                           "FROM Traits INNER JOIN (Plots INNER JOIN PlotData ON Plots.PlotID = PlotData.PlotID) ON " +
                           "Traits.TraitID = PlotData.TraitID " +
                           "WHERE (((Plots.TreatmentID)=" + TreatmentID + ")) " +
                           "GROUP BY Plots.TreatmentID, PlotData.Date, Plots.Rep " +
                           "ORDER BY PlotData.Date, Plots.Rep PIVOT Traits.Trait;";
                }
                else if (DataSource == "Crop")
                {
                    SQL = "TRANSFORM Avg(PlotData.Value) AS AvgOfValue " +
                            "SELECT Plots.TreatmentID, PlotData.Date " +
                            "FROM Plots INNER JOIN (Traits INNER JOIN PlotData ON Traits.TraitID = " +
                            "PlotData.TraitID) ON Plots.PlotID = PlotData.PlotID " +
                            "WHERE (((Plots.TreatmentID)=" + TreatmentID + ")) " +
                            "GROUP BY Plots.TreatmentID, PlotData.Date " +
                            "ORDER BY PlotData.Date PIVOT Traits.Trait;";
                }

                cmd = new OleDbCommand(SQL, con); 
                reader = cmd.ExecuteReader(); 
                DataTable Table = new DataTable();
                Table.Load(reader);
                Table.Columns.Add("Title", typeof(string));
                foreach (DataRow Row in Table.Rows)
                    Row["Title"] = Experiment + "-" + Treatment;
                return Table;
            }
            catch (Exception)
            {

            }
            finally
            {
                con.Close();  // finally closes connection
            }
        }
        return null;
    }

    #endregion

    /// <summary>
    /// Merge all columns and row from source into dest.
    /// </summary>
    private void Merge(DataTable Source, DataTable Dest)
    {
        // Can't use the DataTable.Merge method to do this because it can't cope with putting
        // strings into ints. Get mismatch in datatype error messages.

        if (Source.Rows.Count > 0)
        {
            foreach (DataColumn Col in Source.Columns)
            {
                if (!Dest.Columns.Contains(Col.ColumnName))
                    Dest.Columns.Add(Col.ColumnName, Col.DataType);
            }

            foreach (DataRow Row in Source.Rows)
            {
                DataRow NewRow = Dest.NewRow();
                Dest.Rows.Add(NewRow);
                foreach (DataColumn Col in Source.Columns)
                {
                    try
                    {
                        NewRow[Col.ColumnName] = Row[Col];
                    }
                    catch (Exception)
                    {
                        // Data type mistmatch
                    }
                }


            }
        }
    }



}
   
