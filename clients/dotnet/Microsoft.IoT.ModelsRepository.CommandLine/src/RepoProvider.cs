﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Azure.IoT.ModelsRepository;
using Microsoft.Azure.DigitalTwins.Parser;
using Microsoft.IoT.ModelsRepository.Extensions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Microsoft.IoT.ModelsRepository.CommandLine
{
    internal class RepoProvider
    {
        readonly ModelsRepositoryClient _repositoryClient;
        readonly string _repoLocationUriStr;
        readonly Uri _repoLocationUri;

        public RepoProvider(string repoLocationUriStr)
        {
            _repoLocationUriStr = repoLocationUriStr;
            if (IsRelativePath(_repoLocationUriStr))
            {
                _repoLocationUriStr = Path.GetFullPath(_repoLocationUriStr);
            }

            _repoLocationUri = new Uri(_repoLocationUriStr);
            _repositoryClient = new ModelsRepositoryClient(_repoLocationUri);
        }

        public async Task<List<string>> ExpandModel(FileInfo modelFile)
        {
            string dtmi = ParsingUtils.GetRootId(modelFile);
            return await ExpandModel(dtmi);
        }

        /// <summary>
        /// Uses a combination of the Model Parser and DMR Client to produce expanded model format.
        /// This method is implemented such that the Model Parser will drive model dependency resolution,
        /// as opposed to the DMR client doing so.
        /// </summary>
        public async Task<List<string>> ExpandModel(string dtmi)
        {
            var totalDependentReferences = new Dictionary<string, string>();
            ModelResult rootModelResult = await _repositoryClient.GetModelAsync(dtmi, ModelDependencyResolution.Disabled);
            totalDependentReferences.Add(dtmi, rootModelResult.Content[dtmi]);

            var parser = new ModelParser
            {
                DtmiResolver = async (IReadOnlyCollection<Dtmi> dtmis) =>
                {
                    IEnumerable<string> dtmiStrings = dtmis.Select(s => s.AbsoluteUri);
                    var dependentReferences = new Dictionary<string, string>();
                    foreach (string dtmi in dtmiStrings)
                    {
                        if (!totalDependentReferences.ContainsKey(dtmi))
                        {
                            ModelResult modelResult = await _repositoryClient.GetModelAsync(dtmi, ModelDependencyResolution.Disabled);
                            totalDependentReferences.Add(dtmi, modelResult.Content[dtmi]);
                        }

                        dependentReferences[dtmi] = totalDependentReferences[dtmi];
                    }

                    return dependentReferences.Values.ToList();
                }
            };
            await parser.ParseAsync(rootModelResult.Content.Values.ToList());
            return ConvertToExpanded(dtmi, totalDependentReferences);
        }

        private List<string> ConvertToExpanded(string rootDtmi, IDictionary<string, string> models)
        {
            var result = new List<string>
            {
                models[rootDtmi]
            };
            models.Remove(rootDtmi);
            result.AddRange(models.Values);

            return result;
        }

        public ModelParser GetDtdlParser()
        {
            var parser = new ModelParser
            {
                DtmiResolver = _repositoryClient.ParserDtmiResolver
            };
            return parser;
        }

        // TODO: Convert to instance method.
        public static bool IsRelativePath(string repositoryPath)
        {
            bool validUri = Uri.TryCreate(repositoryPath, UriKind.Relative, out Uri testUri);
            return validUri && testUri != null;
        }

        public bool IsRemoteEndpoint()
        {
            bool validUri = Uri.TryCreate(_repoLocationUriStr, UriKind.Absolute, out Uri testUri);
            return validUri && testUri != null && testUri.Scheme != "file";
        }

        public Uri RepoLocationUri
        {
            get { return _repoLocationUri; }
        }
    }
}
