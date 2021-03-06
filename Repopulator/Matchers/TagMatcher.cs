﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CsvHelper;
using CsvHelper.Configuration;
using Dicom;

namespace Repopulator.Matchers
{
    /// <summary>
    /// Matcher that reads the entire CSV file and builds a map of UIDs to rows at a granularity of either sop, series or study
    /// </summary>
    internal class TagMatcher : RepopulatorMatcher
    {
        private string[] _fileList;
        private int _currentFile;

        private CsvToDicomColumn _indexer;
        private DicomTag _indexerTag;
        
        /// <summary>
        /// Map of all the InstanceUIDs described in the CSV and the row values on that CSV line
        /// </summary>
        Dictionary<string,string[]> _indexerToRowMap = new Dictionary<string, string[]>();
        
        public TagMatcher(CsvToDicomTagMapping map, DicomRepopulatorOptions options):base(map,options)
        {
            _fileList = GetFileList().ToArray();

            _indexer = GetBestIndexer(map);

            if(_indexer == null)
                throw new ArgumentException("No valid indexer could be found, there must be a column in the map for either SOP, Series or Study instance UIDs");

            using (var reader = new CsvReader(map.CsvFile.OpenText(), new CsvConfiguration(System.Globalization.CultureInfo.CurrentCulture)
            {
                TrimOptions = TrimOptions.Trim
            }))
            {
                while (reader.Read())
                {
                    string key = reader[_indexer.Index];

                    if(_indexerToRowMap.ContainsKey(key))
                        throw new Exception($"Multiple Csv rows describe the same '{_indexerTag}' '{key}'.  Error was on CSV line number '{reader.Parser.RawRow}'");

                    _indexerToRowMap.Add(key,reader.Parser.Record.ToArray());
                }
            }
        }

        private CsvToDicomColumn GetBestIndexer(CsvToDicomTagMapping map)
        {
            return
                GetIndexer(map,DicomTag.SOPInstanceUID)??
                GetIndexer(map,DicomTag.SeriesInstanceUID)??
                GetIndexer(map,DicomTag.StudyInstanceUID);
        }

        private CsvToDicomColumn GetIndexer(CsvToDicomTagMapping map, DicomTag tag)
        {
            var match = map.TagColumns.FirstOrDefault(c => c.TagsToPopulate.Contains(tag));
           
            if (match == null)
                return null;

            _indexerTag = tag;

            return match;
        }

        public override RepopulatorJob Next()
        {
            //we have run out of files to process
            if (_currentFile >= _fileList.Length)
                return null;

            try
            {
                var currentFile = new FileInfo(_fileList[_currentFile]);
                var df = DicomFile.Open(currentFile.FullName);
                var seek = df.Dataset.GetValue<string>(_indexerTag, 0);

                if(!_indexerToRowMap.ContainsKey(seek))
                    throw new Exception($"Csv did not contain a value for {_indexerTag} {seek} which was found in file '{_fileList[_currentFile]}'");

                return new RepopulatorJob(Map,currentFile,df,_indexerToRowMap[seek]);
            }
            finally
            {
                _currentFile++;
            }
        }

        public override int GetInputFileCount()
        {
            return _fileList.Length;
        }

        public override void Dispose()
        {

        }
    }
}