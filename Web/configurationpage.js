export default function (view) {
    view.addEventListener('viewshow', async () => {
        const pluginId = '16445656-5f0a-4ea5-8e04-00d8f9c935c9';

        const config = await ApiClient.getPluginConfiguration(pluginId);

        view.querySelector('#movieExpirationDays').value = config.MovieExpirationDays;
        view.querySelector('#seasonExpirationDays').value = config.SeasonExpirationDays;
        view.querySelector('#unwatchedMovieExpirationDays').value = config.UnwatchedMovieExpirationDays;
        view.querySelector('#unwatchedSeasonExpirationDays').value = config.UnwatchedSeasonExpirationDays;
        view.querySelector('#expiringSoonWindowDays').value = config.ExpiringSoonWindowDays;
        view.querySelector('#diskSpaceThresholdGb').value = config.DiskSpaceThresholdGb;
        view.querySelector('#diskSpaceMonitorPath').value = config.DiskSpaceMonitorPath;
        view.querySelector('#scheduledTaskHour').value = config.ScheduledTaskHour;
        view.querySelector('#dryRun').checked = config.DryRun;

        view.querySelector('#MediaExpirationConfigForm').addEventListener('submit', async (e) => {
            e.preventDefault();

            const updated = await ApiClient.getPluginConfiguration(pluginId);

            updated.MovieExpirationDays = parseInt(view.querySelector('#movieExpirationDays').value);
            updated.SeasonExpirationDays = parseInt(view.querySelector('#seasonExpirationDays').value);
            updated.UnwatchedMovieExpirationDays = parseInt(view.querySelector('#unwatchedMovieExpirationDays').value);
            updated.UnwatchedSeasonExpirationDays = parseInt(view.querySelector('#unwatchedSeasonExpirationDays').value);
            updated.ExpiringSoonWindowDays = parseInt(view.querySelector('#expiringSoonWindowDays').value);
            updated.DiskSpaceThresholdGb = parseInt(view.querySelector('#diskSpaceThresholdGb').value);
            updated.DiskSpaceMonitorPath = view.querySelector('#diskSpaceMonitorPath').value;
            updated.ScheduledTaskHour = parseInt(view.querySelector('#scheduledTaskHour').value);
            updated.DryRun = view.querySelector('#dryRun').checked;

            await ApiClient.updatePluginConfiguration(pluginId, updated);
            Dashboard.processPluginConfigurationUpdateResult();
        });
    });
}