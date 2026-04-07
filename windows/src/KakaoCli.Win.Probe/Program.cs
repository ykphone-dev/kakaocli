using KakaoCli.Win.Automation;
using KakaoCli.Win.Core.Utilities;
using KakaoCli.Win.Data;

var store = new ProbeArtifactStore();
var collector = new WindowsProbeCollector(store);
collector.WriteArtifacts();
JsonOutput.Write(collector.CollectSummary());
