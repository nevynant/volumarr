using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration;

[Migration(233)]
public class add_audible_series_map : NzbDroneMigrationBase
{
    protected override void MainDbUpgrade()
    {
        Create.TableForModel("AudibleSeriesMap")
              .WithColumn("Asin").AsString().NotNullable().Unique();
    }
}
