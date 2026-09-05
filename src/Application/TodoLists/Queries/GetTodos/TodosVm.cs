using MyWealthV2.Application.Common.Models;

namespace MyWealthV2.Application.TodoLists.Queries.GetTodos;

public class TodosVm
{
    public IReadOnlyCollection<LookupDto> PriorityLevels { get; init; } = [];

    public IReadOnlyCollection<ColourDto> Colours { get; init; } = [];

    public IReadOnlyCollection<TodoListDto> Lists { get; init; } = [];
}

public class ColourDto
{
    public string Code { get; init; } = string.Empty;

    public string Name { get; init; } = string.Empty;
}
