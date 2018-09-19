using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;
using System.Data;

namespace NzbDrone.Core.Datastore.Migration
{
    [Migration(152)]
    public class add_allflicksurl : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
	    if (!this.Schema.Schema("dbo").Table("Movies").Column("NetflixUrl").Exists())
	    {
             Alter.Table("Movies").AddColumn("NetflixUrl").AsString().Nullable();
        }
        if (!this.Schema.Schema("dbo").Table("Movies").Column("PrimeVideoUrl").Exists())
        {
             Alter.Table("Movies").AddColumn("PrimeVideoUrl").AsString().Nullable();
        }
        if (!this.Schema.Schema("dbo").Table("Movies").Column("HooplaUrl").Exists())
        {
             Alter.Table("Movies").AddColumn("HooplaUrl").AsString().Nullable();
        }
        if (!this.Schema.Schema("dbo").Table("Movies").Column("TubiUrl").Exists())
        {
             Alter.Table("Movies").AddColumn("TubiUrl").AsString().Nullable();
        }
        if (!this.Schema.Schema("dbo").Table("Movies").Column("JustWatchUrl").Exists())
        {
             Alter.Table("Movies").AddColumn("JustWatchUrl").AsString().Nullable();
        }
	}
    }
}
