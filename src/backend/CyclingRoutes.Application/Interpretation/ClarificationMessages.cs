namespace CyclingRoutes.Application.Interpretation;

internal static class ClarificationMessages
{
	public static string Get(string locale, string field, string code)
	{
		var index = locale switch { "ru" => 1, "he" => 2, _ => 0 };
		var label = field switch
		{
			"start" => Pick("Start", "Старт", "נקודת התחלה"),
			"destination" => Pick("Destination", "Финиш", "יעד"),
			"shape" => Pick("Route shape", "Тип маршрута", "סוג מסלול"),
			"profile" => Pick("Cycling profile", "Покрытие", "סוג רכיבה"),
			"elevation" => Pick("Elevation preference", "Предпочтение по подъёмам", "העדפת עליות"),
			"targetDistanceMeters" => Pick("Distance", "Расстояние", "מרחק"),
			"targetDurationSeconds" => Pick("Duration", "Длительность", "משך"),
			"targetDistanceRangeMeters" => Pick("Distance range", "Диапазон расстояния", "טווח מרחק"),
			"targetDurationRangeSeconds" => Pick("Duration range", "Диапазон длительности", "טווח משך"),
			"targetDistanceRangeMeters.min" => Pick("Minimum distance", "Минимальное расстояние", "מרחק מינימלי"),
			"targetDistanceRangeMeters.max" => Pick("Maximum distance", "Максимальное расстояние", "מרחק מרבי"),
			"targetDurationRangeSeconds.min" => Pick("Minimum duration", "Минимальная длительность", "משך מינימלי"),
			"targetDurationRangeSeconds.max" => Pick("Maximum duration", "Максимальная длительность", "משך מרבי"),
			"start.latitude" or "destination.latitude" => Pick("Latitude", "Широта", "קו רוחב"),
			"start.longitude" or "destination.longitude" => Pick("Longitude", "Долгота", "קו אורך"),
			_ => Pick("Request", "Запрос", "בקשה")
		};
		var instruction = code switch
		{
			"required" => Pick("Please specify a value.", "Укажите значение.", "נא לציין ערך."),
			"target_required" => Pick("Specify distance or duration.", "Укажите расстояние или длительность.", "נא לציין מרחק או משך."),
			"must_be_positive" => Pick("Use a value greater than zero.", "Укажите значение больше нуля.", "נא לציין ערך גדול מאפס."),
			"out_of_range" => Pick("Use a value within the supported range.", "Укажите значение в допустимом диапазоне.", "נא לציין ערך בטווח המותר."),
			"range_reversed" => Pick("The minimum must not exceed the maximum.", "Минимум не должен превышать максимум.", "המינימום לא יכול להיות גדול מהמקסימום."),
			"target_conflict" => Pick("Specify a scalar or a range, not both.", "Укажите одно значение или диапазон, но не оба.", "נא לציין ערך יחיד או טווח, לא את שניהם."),
			"route_shape_conflict" => Pick("The selected route shape conflicts with the prompt. Change the selection or revise the prompt.", "Выбранный тип маршрута противоречит запросу. Измените выбор или уточните запрос.", "סוג המסלול שנבחר סותר את הבקשה. שנו את הבחירה או עדכנו את הבקשה."),
			"destination_not_allowed" => Pick("Remove the destination for a loop.", "Для кольцевого маршрута уберите финиш.", "במסלול מעגלי יש להסיר את היעד."),
			"must_differ_from_start" => Pick("Choose a destination different from the start.", "Выберите финиш, отличный от старта.", "נא לבחור יעד שונה מנקודת ההתחלה."),
			"location_requires_map_selection" => Pick("Select this location on the map and remove the location text from the revised prompt.", "Выберите место на карте и уберите его описание из нового запроса.", "בחרו את המיקום במפה והסירו את תיאור המיקום מהבקשה המעודכנת."),
			"ambiguous" => Pick("Clarify which value you want.", "Уточните желаемое значение.", "נא להבהיר מהו הערך הרצוי."),
			_ => Pick("Correct this value.", "Исправьте значение.", "נא לתקן את הערך.")
		};
		return label + ": " + instruction;
		string Pick(string en, string ru, string he) => new[] { en, ru, he }[index];
	}
}
