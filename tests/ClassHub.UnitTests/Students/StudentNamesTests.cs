using ClassHub.Core.Modules.Students.Domain;

namespace ClassHub.UnitTests.Students;

public sealed class StudentNamesTests
{
    [Theory]
    [InlineData("Ана Петровска", "Ана", "Петровска")]
    [InlineData("  Ана   Марија  Петровска ", "Ана", "Марија Петровска")]
    [InlineData("Ана", "Ана", null)]
    [InlineData("   ", "", null)]
    public void Full_names_split_into_first_and_last(string full, string first, string? last) =>
        StudentNames.Split(full).ShouldBe((first, last));

    [Fact]
    public void Long_names_are_cut_to_the_column_length()
    {
        var (first, last) = StudentNames.Split($"{new string('a', 80)} {new string('b', 80)}");
        first.Length.ShouldBe(Student.NameMaxLength);
        last!.Length.ShouldBe(Student.NameMaxLength);
    }

    [Theory]
    [InlineData(" Ana@Example.TEST ", "ana@example.test")]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Emails_are_compared_trimmed_and_lowercase(string? email, string? expected) =>
        StudentNames.NormalizeEmail(email).ShouldBe(expected);
}
