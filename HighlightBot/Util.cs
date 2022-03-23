using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace HighlightBot; 

public static class Util {
	public static IServiceCollection ConfigureDbContext<TDbContext>(this IServiceCollection isc) where TDbContext : DbContext {
		isc.AddDbContext<TDbContext>((isp, dbcob) => {
			ConnectionStringsConfiguration connectionStrings = isp.GetRequiredService<IOptions<ConnectionStringsConfiguration>>().Value;
			_ = connectionStrings.Mode switch {
				ConnectionStringsConfiguration.Backend.Sqlite => dbcob.UseSqlite(connectionStrings.GetConnectionString<TDbContext>()),
				ConnectionStringsConfiguration.Backend.Postgres => dbcob.UseNpgsql(connectionStrings.GetConnectionString<TDbContext>()),
				//_ => throw new ArgumentOutOfRangeException(nameof(connectionStrings.Mode))
			};
		}, ServiceLifetime.Transient);
		return isc;
	}

	// TODO move to Foxite.Common
	// TODO unit testing (in Foxite.Common)
	public static string JoinOxfordComma(ICollection<string> strings) {
		var ret = new StringBuilder();
		int i = 0;
		foreach (string str in strings) {
			if (i > 0) {
				if (strings.Count != 2) {
					ret.Append(',');
				}

				ret.Append(' ');

				if (i == strings.Count - 1) {
					ret.Append("and ");
				}
			}

			ret.Append(str);
			
			i++;
		}

		return ret.ToString();
	}

	// TODO move to Foxite.Common
	public static string Ellipsis(this string str, int maxLength, string ellipsis = "…") {
		if (str.Length > maxLength) {
			return str[..(maxLength - ellipsis.Length)] + ellipsis;
		} else {
			return str;
		}
	}
}
