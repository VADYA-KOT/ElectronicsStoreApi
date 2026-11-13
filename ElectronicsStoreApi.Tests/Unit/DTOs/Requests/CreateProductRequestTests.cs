using ElectronicsStoreApi.DTOs.Requests;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Xunit;

namespace ElectronicsStoreApi.Tests;

public class CreateProductRequestTests
{
    [Fact]
    public void ValidRequest_ShouldPassValidation()
    {
        var request = new CreateProductRequest
        {
            Name = "Мышь",
            Price = 2500,
            Quantity = 5,
            CategoryId = 1,
            Description = "Игровая мышь"
        };

        var errors = Validate(request);

        Assert.Empty(errors);
    }

    [Fact]
    public void EmptyName_ShouldFailValidation()
    {
        var request = new CreateProductRequest
        {
            Name = "",
            Price = 2500,
            Quantity = 5
        };

        var errors = Validate(request);

        Assert.Contains(errors, error =>
            error.MemberNames.Contains(
                nameof(CreateProductRequest.Name)));
    }

    [Fact]
    public void ZeroPrice_ShouldFailValidation()
    {
        var request = new CreateProductRequest
        {
            Name = "Мышь",
            Price = 0,
            Quantity = 5
        };

        var errors = Validate(request);

        Assert.Contains(errors, error =>
            error.MemberNames.Contains(
                nameof(CreateProductRequest.Price)));
    }

    [Fact]
    public void NegativeQuantity_ShouldFailValidation()
    {
        var request = new CreateProductRequest
        {
            Name = "Мышь",
            Price = 2500,
            Quantity = -1
        };

        var errors = Validate(request);

        Assert.Contains(errors, error =>
            error.MemberNames.Contains(
                nameof(CreateProductRequest.Quantity)));
    }

    private static List<ValidationResult> Validate(object instance)
    {
        var context = new ValidationContext(instance);
        var errors = new List<ValidationResult>();

        Validator.TryValidateObject(
            instance,
            context,
            errors,
            validateAllProperties: true);

        return errors;
    }

    [Fact]
    public void NameWith200Characters_ShouldPassValidation()
    {
        var request = new CreateProductRequest
        {
            Name = new string('A', 200),
            Price = 2500,
            Quantity = 5
        };

        var errors = Validate(request);

        Assert.DoesNotContain(errors, error =>
            error.MemberNames.Contains(nameof(CreateProductRequest.Name)));
    }

    [Fact]
    public void NameWith201Characters_ShouldFailValidation()
    {
        var request = new CreateProductRequest
        {
            Name = new string('A', 201),
            Price = 2500,
            Quantity = 5
        };

        var errors = Validate(request);

        Assert.Contains(errors, error =>
            error.MemberNames.Contains(nameof(CreateProductRequest.Name)));
    }
}
