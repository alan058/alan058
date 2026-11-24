# Release process

## Branching and review

1. Branch from `main`: `feature/<ticket>-short-name` or `fix/<ticket>-short-name`.
2. Open a pull request. The template asks for the root cause (for defects),
   an impact assessment and the tests run.
3. CI must pass: build (including the VB.NET app), all tests including SQL
   Server contract tests, a repeated database deployment, and the desktop smoke test.
4. At least one approving review. Address every comment, or reply explaining
   why not.
5. Squash-merge to `main` with the ticket number in the message.

## Database changes

| Change | Where | Rule |
|---|---|---|
| New table, column, index, constraint, reference data | New file in `database/migrations/` named `NNNN_Description.sql` | **Never edit a migration after it has been deployed anywhere.** The deployer stores a checksum and refuses to continue if one changes. |
| New or changed stored procedure | `database/programmability/` (`CREATE OR ALTER`) | Re-applied on every deployment, so edit in place |
| Breaking change (rename/drop a column) | Two releases: add the new thing and deploy the apps that use it; remove the old one in the next release | The desktop app is updated on users' PCs gradually, so old and new versions run side by side for a while |

## Release checklist

**Before**

- [ ] `CHANGELOG.md` updated under a new version heading
- [ ] Version bumped in `Directory.Build.props`
- [ ] Database backup taken (or confirmed recent) on the target server
- [ ] Deployment tested on a copy of production data (UAT)
- [ ] Business stakeholders told about the release window and visible changes

**Deploy**

1. Database: `stockflow-dbdeploy --connection "<target>"`. It applies only new
   migrations, then re-applies all procedures, each in its own transaction.
2. Web app: publish the `StockFlow.Web` build artifact and restart.
3. Desktop app: copy the `stockflow-desktop` CI artifact to the network
   install share.

**After**

- [ ] `GET /health` returns `Healthy`
- [ ] Smoke test: open the dashboard, open an order, run the low-stock report
- [ ] Check `dbo.ErrorLog` and the web logs for new errors over the next hour
- [ ] Tell stakeholders the release is complete

## Rollback

* **Applications:** redeploy the previous build artifact.
* **Procedures:** redeploy the previous release's `database/programmability` with the deployer.
* **Migrations:** they are not rolled back automatically. A bad migration is
  fixed with a new migration (roll forward), or by restoring the pre-release
  backup if data was damaged. This is why breaking schema changes are split
  across two releases.
